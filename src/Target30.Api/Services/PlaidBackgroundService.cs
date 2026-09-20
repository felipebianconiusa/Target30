using Going.Plaid;
using Going.Plaid.Item;
using Microsoft.EntityFrameworkCore;
using Target30.Api;
using Target30.Api.Billing;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Services;

// Roda em background: sincroniza todos os Items do Plaid de todos os usuários periodicamente
// (sem precisar abrir o app) e, depois de cada sync, manda um email pra quem tiver cartão
// precisando de pagamento antes do fechamento (e ainda não foi avisado hoje).
public class PlaidBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlaidBackgroundService> _logger;
    private readonly IHostEnvironment _environment;
    private TimeSpan _syncInterval = TimeSpan.FromHours(6);

    // Usuários com acesso (assinatura em dia, teste grátis ou isento) no ciclo atual. Quem não tem não
    // é sincronizado com o Plaid (custo) nem recebe alertas baseados em dado que deixou de atualizar.
    private static IReadOnlySet<string> s_activeUsers = new HashSet<string>();

    public PlaidBackgroundService(
        IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<PlaidBackgroundService> logger,
        IHostEnvironment environment)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = double.TryParse(_configuration["Sync:IntervalHours"], out var h) ? h : 6;
        var interval = TimeSpan.FromHours(Math.Max(intervalHours, 0.5));
        _syncInterval = interval;

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no sync automático em background");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Target30DbContext>();
        var sync = scope.ServiceProvider.GetRequiredService<PlaidSyncService>();
        var emailSender = scope.ServiceProvider.GetRequiredService<AlertDispatcher>();

        s_activeUsers = await scope.ServiceProvider.GetRequiredService<BillingService>().UsersWithAccessAsync();
        var activeItems = (await db.PlaidItems.ToListAsync(stoppingToken)).Where(i => s_activeUsers.Contains(i.UserId)).ToList();
        var nowUtc = DateTime.UtcNow;
        var items = activeItems.Where(i => SyncSchedule.IsDue(i.LastSyncedAt, nowUtc, _syncInterval)).ToList();
        _logger.LogInformation("Sync automático: {Count} de {Total} item(ns) do Plaid (o resto sincronizou há pouco).", items.Count, activeItems.Count);

        foreach (var item in items)
        {
            try
            {
                await sync.SyncItemAsync(item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha ao sincronizar item {ItemId}", item.ItemId);
            }
        }

        await RunAutomaticBackupAsync(db);
        await SendStaleDataAlertsAsync(db, emailSender, scope.ServiceProvider.GetRequiredService<PlaidClient>());
        await SendLowBalanceAlertsAsync(db, emailSender, scope.ServiceProvider.GetRequiredService<CashFlowService>());
        await SendAlertsAsync(db, emailSender);
        await SendWeeklyDigestsAsync(db, emailSender);
        await SendBudgetAlertsAsync(db, emailSender);
        await SendSubscriptionPriceChangeAlertsAsync(db, emailSender);
    }

    // Backup diário do banco (Backup:Directory, Backup:IntervalHours, Backup:KeepCount). Roda a cada
    // ciclo do sync mas só cria um arquivo novo quando o último passou do intervalo.
    private async Task RunAutomaticBackupAsync(Target30DbContext db)
    {
        try
        {
            var settings = BackupSettings.From(_configuration, _environment.ContentRootPath);
            if (!settings.Enabled)
                return;

            var now = DateTime.UtcNow;
            var last = DatabaseBackup.List(settings.Directory).FirstOrDefault()?.CreatedUtc;
            if (!DatabaseBackup.IsDue(last, now, settings.Interval))
                return;

            var file = await DatabaseBackup.CreateAsync(db, settings.Directory, now);
            DatabaseBackup.Prune(settings.Directory, settings.KeepCount);
            _logger.LogInformation("Backup automático criado: {Path}", file.Path);
        }
        catch (Exception ex)
        {
            // Backup nunca pode derrubar o sync nem os alertas.
            _logger.LogError(ex, "Falha no backup automático");
        }
    }

    // Avisa por email quando o Plaid deixa de atualizar um banco (dado velho ou conexão com
    // erro). Um email por problema (DataFreshness.ShouldAlert), não a cada sync. /item/get é grátis.
    private async Task SendStaleDataAlertsAsync(Target30DbContext db, AlertDispatcher emailSender, PlaidClient client)
    {
        var now = DateTimeOffset.UtcNow;
        var items = (await db.PlaidItems.ToListAsync()).Where(i => s_activeUsers.Contains(i.UserId)).ToList();
        var problemsByUser = new Dictionary<string, List<(PlaidItem Item, FreshnessStatus Status, DateTimeOffset? Last)>>();

        foreach (var item in items)
        {
            try
            {
                var r = await client.ItemGetAsync(new ItemGetRequest { AccessToken = item.AccessToken });
                var tx = r.Status?.Transactions;
                var status = DataFreshness.Evaluate(
                    tx?.LastSuccessfulUpdate, tx?.LastFailedUpdate, r.Error?.ErrorCode ?? r.Item?.Error?.ErrorCode, now);

                if (status == FreshnessStatus.Ok)
                {
                    item.LastStaleAlertSentAt = null;
                    continue;
                }

                if (!DataFreshness.ShouldAlert(status, item.LastStaleAlertSentAt))
                    continue;

                if (!problemsByUser.TryGetValue(item.UserId, out var list))
                    problemsByUser[item.UserId] = list = [];
                list.Add((item, status, tx?.LastSuccessfulUpdate));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Não foi possível checar a atualização do item {ItemId}", item.ItemId);
            }
        }

        foreach (var (userId, problems) in problemsByUser)
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            if (settings is null || !settings.NotificationsEnabled || !AlertDispatcher.HasChannel(settings))
                continue;

            var body = DataFreshness.BuildAlertBody(
                problems.Select(p => (p.Item.InstitutionName ?? "Banco", p.Status, p.Last)), now);
            await emailSender.SendAsync(settings, "Target30: dados de um banco podem estar desatualizados", body);

            foreach (var p in problems)
                p.Item.LastStaleAlertSentAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    // Avisa por email quando o saldo projetado das contas correntes (Fluxo de Caixa) vai ficar
    // abaixo do limite escolhido nos próximos 30 dias. Um email por problema (LowBalanceWarning.Key).
    private async Task SendLowBalanceAlertsAsync(Target30DbContext db, AlertDispatcher emailSender, CashFlowService cashFlow)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var userIds = (await db.PlaidItems.Select(i => i.UserId).Distinct().ToListAsync()).Where(s_activeUsers.Contains).ToList();

        foreach (var userId in userIds)
        {
            try
            {
                var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
                if (settings is null || !settings.NotificationsEnabled || !AlertDispatcher.HasChannel(settings))
                    continue;

                var (_, currentBalance, rows) = await cashFlow.BuildRowsAsync(userId, 0, 30, today);
                var warning = LowBalanceDetector.Find(rows, currentBalance, settings.LowBalanceThreshold, today);

                if (warning is null)
                {
                    settings.LastLowBalanceAlertKey = null;
                    continue;
                }

                if (warning.Key == settings.LastLowBalanceAlertKey)
                    continue;

                await emailSender.SendAsync(
                    settings, "Target30: saldo baixo à vista",
                    LowBalanceDetector.BuildAlertBody(warning, settings.LowBalanceThreshold));
                settings.LastLowBalanceAlertKey = warning.Key;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao checar saldo baixo do usuário {UserId}", userId);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SendAlertsAsync(Target30DbContext db, AlertDispatcher emailSender)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var userIds = (await db.PlaidItems.Select(i => i.UserId).Distinct().ToListAsync()).Where(s_activeUsers.Contains).ToList();

        foreach (var userId in userIds)
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            if (settings is null || !settings.NotificationsEnabled || !AlertDispatcher.HasChannel(settings))
                continue;

            var cards = await db.PlaidAccounts.Where(a => a.UserId == userId && a.Type == "Credit").ToListAsync();
            var alerts = new List<string>();

            foreach (var card in cards)
            {
                if (card.LastAlertSentDate == today)
                    continue;

                var p = CardMath.Compute(card, settings.GlobalTargetUtilizationPercent, today);
                var needsAlert = p.DaysUntilPaymentDeadline is not null
                    && p.DaysUntilPaymentDeadline <= settings.NotifyDaysBeforeClosing
                    && p.AmountToPay > 0;

                if (!needsAlert)
                    continue;

                alerts.Add($"{card.DisplayName}: pague {p.AmountToPay:C} até {p.PaymentDeadline:yyyy-MM-dd} pra fechar em {p.TargetPercent}%.");
                card.LastAlertSentDate = today;
            }

            if (alerts.Count > 0)
            {
                var body = "Target30 — cartões precisando de pagamento antes do fechamento:\n\n" + string.Join("\n", alerts);
                await emailSender.SendAsync(settings, "Target30: pagamento necessário antes do fechamento", body);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SendWeeklyDigestsAsync(Target30DbContext db, AlertDispatcher emailSender)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var userIds = (await db.PlaidItems.Select(i => i.UserId).Distinct().ToListAsync()).Where(s_activeUsers.Contains).ToList();

        foreach (var userId in userIds)
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            if (settings is null || !settings.WeeklyDigestEnabled || !AlertDispatcher.HasChannel(settings))
                continue;

            if (!WeeklyDigest.ShouldSend(settings.LastDigestSentDate, today))
                continue;

            var cards = await db.PlaidAccounts.Where(a => a.UserId == userId && a.Type == "Credit").ToListAsync();
            if (cards.Count == 0)
                continue;

            var withProjections = cards
                .Select(c => (Account: c, Projection: CardMath.Compute(c, settings.GlobalTargetUtilizationPercent, today)))
                .ToList();

            var body = WeeklyDigest.BuildBody(withProjections, today);
            await emailSender.SendAsync(settings, "Target30: resumo semanal dos seus cartões", body);
            settings.LastDigestSentDate = today;
        }

        await db.SaveChangesAsync();
    }

    private static async Task SendBudgetAlertsAsync(Target30DbContext db, AlertDispatcher emailSender)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentMonthKey = $"{today.Year:D4}-{today.Month:D2}";
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        var userIds = (await db.PlaidItems.Select(i => i.UserId).Distinct().ToListAsync()).Where(s_activeUsers.Contains).ToList();

        foreach (var userId in userIds)
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            if (settings is null || !settings.NotificationsEnabled || !AlertDispatcher.HasChannel(settings))
                continue;

            var budgets = await db.CategoryBudgets.Where(b => b.UserId == userId).ToListAsync();
            if (budgets.Count == 0)
                continue;

            var spend = await db.PlaidTransactions
                .ExcludingInternalTransfers()
                .Where(t => t.UserId == userId && t.Amount > 0 && t.Date >= firstOfMonth && t.Date <= today)
                .GroupBy(t => (t.UserCategory ?? t.Category) ?? "OUTROS")
                .Select(g => new { Category = g.Key, Total = g.Sum(t => t.Amount) })
                .ToDictionaryAsync(g => g.Category, g => g.Total);

            var overBudget = new List<string>();
            foreach (var budget in budgets)
            {
                if (budget.LastAlertSentMonth == currentMonthKey)
                    continue;

                var spent = spend.GetValueOrDefault(budget.Category, 0m);
                if (spent <= budget.MonthlyLimit)
                    continue;

                var percent = budget.MonthlyLimit > 0 ? Math.Round(spent / budget.MonthlyLimit * 100, 0) : 0;
                overBudget.Add($"{budget.Category}: gastou {spent:C} de {budget.MonthlyLimit:C} ({percent}%)");
                budget.LastAlertSentMonth = currentMonthKey;
            }

            if (overBudget.Count > 0)
            {
                var body = "Target30 — categorias que estouraram o orçamento mensal:\n\n" + string.Join("\n", overBudget);
                await emailSender.SendAsync(settings, "Target30: orçamento mensal estourado", body);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SendSubscriptionPriceChangeAlertsAsync(Target30DbContext db, AlertDispatcher emailSender)
    {
        var userIds = (await db.PlaidItems.Select(i => i.UserId).Distinct().ToListAsync()).Where(s_activeUsers.Contains).ToList();

        foreach (var userId in userIds)
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            if (settings is null || !settings.NotificationsEnabled || !AlertDispatcher.HasChannel(settings))
                continue;

            var bills = await db.RecurringBills.Where(b => b.UserId == userId && b.IsActive).ToListAsync();
            if (bills.Count == 0)
                continue;

            var transactions = await db.PlaidTransactions
                .ExcludingInternalTransfers()
                .Where(t => t.UserId == userId && t.Amount > 0 && !t.Pending)
                .ToListAsync();

            var changes = new List<string>();
            foreach (var bill in bills)
            {
                var merchantTxs = transactions
                    .Where(t => (t.MerchantName ?? t.Name).Equals(bill.Description, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var detected = SubscriptionDetector.Detect(merchantTxs);
                if (detected is null)
                    continue;

                var tolerance = Math.Max(1m, bill.Amount * 0.05m);
                if (Math.Abs(detected.AverageAmount - bill.Amount) <= tolerance)
                    continue;
                if (bill.LastPriceAlertAmount == detected.AverageAmount)
                    continue; // já avisamos dessa mudança específica

                changes.Add($"{bill.Description}: de {bill.Amount:C} para {detected.AverageAmount:C}");
                bill.LastPriceAlertAmount = detected.AverageAmount;
            }

            if (changes.Count > 0)
            {
                var body = "Target30 — o valor de assinaturas/contas recorrentes mudou:\n\n" + string.Join("\n", changes)
                    + "\n\nAtualize em Fluxo de Caixa > Gerenciar contas recorrentes.";
                await emailSender.SendAsync(settings, "Target30: valor de assinatura mudou", body);
            }
        }

        await db.SaveChangesAsync();
    }
}
