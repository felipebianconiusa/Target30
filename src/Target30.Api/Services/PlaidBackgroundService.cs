using Microsoft.EntityFrameworkCore;
using Target30.Api;
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

    public PlaidBackgroundService(
        IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<PlaidBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalHours = double.TryParse(_configuration["Sync:IntervalHours"], out var h) ? h : 6;
        var interval = TimeSpan.FromHours(Math.Max(intervalHours, 0.5));

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
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var items = await db.PlaidItems.ToListAsync(stoppingToken);
        _logger.LogInformation("Sync automático: {Count} item(ns) do Plaid.", items.Count);

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

        await SendAlertsAsync(db, emailSender);
        await SendWeeklyDigestsAsync(db, emailSender);
        await SendBudgetAlertsAsync(db, emailSender);
        await SendSubscriptionPriceChangeAlertsAsync(db, emailSender);
    }

    private static async Task SendAlertsAsync(Target30DbContext db, IEmailSender emailSender)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var userIds = await db.PlaidItems.Select(i => i.UserId).Distinct().ToListAsync();

        foreach (var userId in userIds)
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            if (settings is null || !settings.NotificationsEnabled || string.IsNullOrWhiteSpace(settings.Email))
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
                await emailSender.SendAsync(settings.Email!, "Target30: pagamento necessário antes do fechamento", body);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SendWeeklyDigestsAsync(Target30DbContext db, IEmailSender emailSender)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var userIds = await db.PlaidItems.Select(i => i.UserId).Distinct().ToListAsync();

        foreach (var userId in userIds)
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            if (settings is null || !settings.WeeklyDigestEnabled || string.IsNullOrWhiteSpace(settings.Email))
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
            await emailSender.SendAsync(settings.Email!, "Target30: resumo semanal dos seus cartões", body);
            settings.LastDigestSentDate = today;
        }

        await db.SaveChangesAsync();
    }

    private static async Task SendBudgetAlertsAsync(Target30DbContext db, IEmailSender emailSender)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentMonthKey = $"{today.Year:D4}-{today.Month:D2}";
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        var userIds = await db.PlaidItems.Select(i => i.UserId).Distinct().ToListAsync();

        foreach (var userId in userIds)
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            if (settings is null || !settings.NotificationsEnabled || string.IsNullOrWhiteSpace(settings.Email))
                continue;

            var budgets = await db.CategoryBudgets.Where(b => b.UserId == userId).ToListAsync();
            if (budgets.Count == 0)
                continue;

            var spend = await db.PlaidTransactions
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
                await emailSender.SendAsync(settings.Email!, "Target30: orçamento mensal estourado", body);
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task SendSubscriptionPriceChangeAlertsAsync(Target30DbContext db, IEmailSender emailSender)
    {
        var userIds = await db.PlaidItems.Select(i => i.UserId).Distinct().ToListAsync();

        foreach (var userId in userIds)
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync(s => s.UserId == userId);
            if (settings is null || !settings.NotificationsEnabled || string.IsNullOrWhiteSpace(settings.Email))
                continue;

            var bills = await db.RecurringBills.Where(b => b.UserId == userId && b.IsActive).ToListAsync();
            if (bills.Count == 0)
                continue;

            var transactions = await db.PlaidTransactions
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
                await emailSender.SendAsync(settings.Email!, "Target30: valor de assinatura mudou", body);
            }
        }

        await db.SaveChangesAsync();
    }
}
