using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CardsController : ControllerBase
{
    private readonly Target30DbContext _db;

    public CardsController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Cartões de crédito conectados, com utilização atual, meta e quanto pagar pra bater a
    // meta antes do fechamento. StatementClosingDay é o dia do mês configurado (sugerido a
    // partir do último extrato do Plaid, editável em PUT).
    [HttpGet]
    public async Task<IActionResult> GetCards()
    {
        var settings = await GetOrCreateSettingsAsync();
        var accounts = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Type == "Credit")
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var cards = accounts
            .Select(a =>
            {
                var p = CardMath.Compute(a, settings.GlobalTargetUtilizationPercent, today);
                var needsAlert = p.DaysUntilPaymentDeadline is not null
                    && p.DaysUntilPaymentDeadline <= settings.NotifyDaysBeforeClosing
                    && p.AmountToPay > 0;

                return new CardDto(
                    a.AccountId,
                    a.Name,
                    a.OfficialName,
                    a.InstitutionName,
                    p.Balance,
                    p.Limit,
                    a.IsoCurrencyCode,
                    p.UtilizationPercent,
                    p.TargetPercent,
                    a.TargetUtilizationPercent is not null,
                    p.AmountToPay,
                    a.StatementClosingDay,
                    p.NextClosingDate,
                    p.PaymentDeadline,
                    p.DaysUntilPaymentDeadline,
                    a.EffectiveNextPaymentDueDate,
                    a.MinimumPaymentAmount,
                    a.IsOverdue,
                    needsAlert,
                    a.ManualCreditLimit,
                    a.ManualNextPaymentDueDate,
                    a.LastAlertSentDate,
                    a.Nickname,
                    a.Owner);
            })
            .OrderBy(c => c.DaysUntilPaymentDeadline ?? int.MaxValue)
            .ToList();

        return Ok(cards);
    }

    // Relatório em CSV com o mesmo cálculo da tela de Cartões (saldo, limite, utilização,
    // quanto pagar, fechamento/vencimento) — pra baixar, guardar ou mandar pra alguém revisar.
    [HttpGet("report")]
    public async Task<IActionResult> DownloadReport()
    {
        var settings = await GetOrCreateSettingsAsync();
        var accounts = (await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Type == "Credit")
            .ToListAsync())
            // Cartões com vencimento definido primeiro (são os acionáveis); dentro de cada
            // grupo, por instituição/nome.
            .OrderByDescending(a => a.EffectiveNextPaymentDueDate != null)
            .ThenBy(a => a.InstitutionName)
            .ThenBy(a => a.Name)
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", new[]
        {
            "Situacao", "Cartao", "Instituicao", "Saldo Atual", "Limite", "Utilizacao %", "Meta %",
            "Valor a Pagar", "Data de Fechamento", "Prazo de Pagamento (dia util)",
            "Data de Vencimento", "Pagamento Minimo", "Em Atraso",
        }.Select(CsvField)));

        foreach (var a in accounts)
        {
            var p = CardMath.Compute(a, settings.GlobalTargetUtilizationPercent, today);
            csv.AppendLine(string.Join(",", new[]
            {
                CsvField(a.EffectiveNextPaymentDueDate is not null ? "Com vencimento" : "Ciclo em aberto"),
                CsvField(a.DisplayName),
                CsvField(a.InstitutionName ?? ""),
                CsvField(p.Balance.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(p.Limit.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(p.UtilizationPercent?.ToString("F1", CultureInfo.InvariantCulture) ?? ""),
                CsvField(p.TargetPercent.ToString("F0", CultureInfo.InvariantCulture)),
                CsvField(p.AmountToPay.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(p.NextClosingDate?.ToString("yyyy-MM-dd") ?? ""),
                CsvField(p.PaymentDeadline?.ToString("yyyy-MM-dd") ?? ""),
                CsvField(a.EffectiveNextPaymentDueDate?.ToString("yyyy-MM-dd") ?? ""),
                CsvField(a.MinimumPaymentAmount?.ToString("F2", CultureInfo.InvariantCulture) ?? ""),
                CsvField(a.IsOverdue == true ? "Sim" : "Nao"),
            }));
        }

        // BOM UTF-8 pra acentuação abrir certo no Excel.
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        var fileName = $"target30-cartoes-{today:yyyy-MM-dd}.csv";
        return File(bytes, "text/csv", fileName);
    }

    private static string CsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    [HttpPut("{accountId}")]
    public async Task<IActionResult> UpdateCard(string accountId, [FromBody] UpdateCardRequest request)
    {
        var account = await _db.PlaidAccounts
            .FirstOrDefaultAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
        if (account is null)
            return NotFound();

        if (request.StatementClosingDay is not null)
            account.StatementClosingDay = Math.Clamp(request.StatementClosingDay.Value, 1, 31);

        account.TargetUtilizationPercent = request.TargetUtilizationPercent;
        account.ManualCreditLimit = request.ManualCreditLimit;
        account.ManualNextPaymentDueDate = request.ManualNextPaymentDueDate;

        var nickname = request.Nickname?.Trim();
        account.Nickname = string.IsNullOrEmpty(nickname) ? null : nickname[..Math.Min(nickname.Length, 60)];
        account.Owner = NormalizeOwner(request.Owner);

        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Histórico de saldo/utilização do cartão (um ponto por dia, capturado a cada sync) —
    // alimenta o gráfico de evolução na tela de Cartões.
    [HttpGet("{accountId}/history")]
    public async Task<IActionResult> GetHistory(string accountId, [FromQuery] int days = 180)
    {
        var ownsAccount = await _db.PlaidAccounts.AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId);
        if (!ownsAccount)
            return NotFound();

        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-Math.Clamp(days, 1, 730));
        var snapshots = await _db.CardBalanceSnapshots
            .Where(s => s.AccountId == accountId && s.UserId == CurrentUserId && s.Date >= since)
            .OrderBy(s => s.Date)
            .Select(s => new CardHistoryPointDto(s.Date, s.Balance, s.Limit, s.UtilizationPercent))
            .ToListAsync();

        return Ok(snapshots);
    }

    // Melhor cartão pra usar hoje: o que demora mais pra fechar a fatura, ignorando os que
    // estouraram o limite (ver BestCardPicker).
    [HttpGet("best-today")]
    public async Task<IActionResult> GetBestCardToday()
    {
        var settings = await GetOrCreateSettingsAsync();
        var accounts = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Type == "Credit")
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var withProjections = accounts
            .Select(a => (Account: a, Projection: CardMath.Compute(a, settings.GlobalTargetUtilizationPercent, today)));

        var (eligible, excluded) = BestCardPicker.Rank(withProjections, today);

        return Ok(new BestCardResponseDto(
            eligible.FirstOrDefault(c => c.Tier != CardTier.Caution) is { } best ? ToBestCardDto(best) : null,
            eligible.Select(ToBestCardDto).ToList(),
            excluded.Select(ToBestCardDto).ToList()));
    }

    // Aparado e limitado; vazio = sem dono.
    private static string? NormalizeOwner(string? owner)
    {
        var trimmed = owner?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed[..Math.Min(trimmed.Length, 40)];
    }

    private static BestCardDto ToBestCardDto(CardRanking r) => new(
        r.Account.AccountId,
        r.Account.Name,
        r.Account.Nickname,
        r.Account.InstitutionName,
        r.Projection.NextClosingDate,
        r.DaysUntilClosing,
        r.Projection.Balance,
        r.Projection.Limit,
        r.AvailableCredit,
        r.Projection.UtilizationPercent,
        r.ExclusionReason switch
        {
            CardExclusionReason.LimitReached => "limit_reached",
            CardExclusionReason.NoClosingDay => "no_closing_day",
            _ => null,
        },
        r.Tier switch
        {
            CardTier.Recommended => "recommended",
            CardTier.Alternative => "alternative",
            CardTier.Caution => "caution",
            _ => null,
        },
        r.Projection.TargetPercent,
        r.OverTarget,
        r.Account.Owner);

    // Dado um valor disponível pra pagar hoje, distribui entre os cartões que precisam de
    // pagamento pra bater a meta — priorizando primeiro quem fecha mais cedo, depois quem
    // está mais acima da meta. Não considera juros/APR: o objetivo aqui é credit score
    // (utilização), não economia de juros.
    [HttpPost("payoff-plan")]
    public async Task<IActionResult> GetPayoffPlan([FromBody] PayoffPlanRequest request)
    {
        var availableAmount = Math.Max(0, request.AvailableAmount);
        var settings = await GetOrCreateSettingsAsync();
        var accounts = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Type == "Credit")
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var candidates = accounts
            .Select(a => (Account: a, Projection: CardMath.Compute(a, settings.GlobalTargetUtilizationPercent, today)))
            .Where(x => x.Projection.AmountToPay > 0)
            .OrderBy(x => x.Projection.DaysUntilPaymentDeadline ?? int.MaxValue)
            .ThenByDescending(x => x.Projection.UtilizationPercent ?? 0)
            .ToList();

        var remaining = availableAmount;
        var allocations = new List<PayoffAllocationDto>();
        foreach (var (account, projection) in candidates)
        {
            if (remaining <= 0)
                break;

            var allocate = Math.Min(remaining, projection.AmountToPay);
            remaining -= allocate;

            var newBalance = projection.Balance - allocate;
            var utilizationAfter = projection.Limit > 0
                ? Math.Round(newBalance / projection.Limit * 100, 1)
                : (decimal?)null;

            var reason = projection.DaysUntilPaymentDeadline is { } days
                ? (days <= 0 ? "Fecha hoje" : $"Fecha em {days} dia(s)")
                : "Sem prazo definido ainda";

            allocations.Add(new PayoffAllocationDto(
                account.AccountId,
                account.Name,
                account.Nickname,
                account.InstitutionName,
                allocate,
                projection.Balance,
                projection.UtilizationPercent,
                utilizationAfter,
                projection.TargetPercent,
                projection.PaymentDeadline,
                projection.DaysUntilPaymentDeadline,
                reason));
        }

        return Ok(new PayoffPlanResponseDto(availableAmount, availableAmount - remaining, remaining, allocations));
    }

    private async Task<UserSettings> GetOrCreateSettingsAsync()
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == CurrentUserId);
        if (settings is null)
        {
            settings = new UserSettings { UserId = CurrentUserId };
            _db.UserSettings.Add(settings);
            await _db.SaveChangesAsync();
        }
        return settings;
    }
}

public record CardDto(
    string AccountId,
    string Name,
    string? OfficialName,
    string? InstitutionName,
    decimal CurrentBalance,
    decimal CreditLimit,
    string? IsoCurrencyCode,
    decimal? UtilizationPercent,
    decimal TargetPercent,
    bool TargetIsCustom,
    decimal AmountToPay,
    int? StatementClosingDay,
    DateOnly? NextClosingDate,
    DateOnly? PaymentDeadline,
    int? DaysUntilPaymentDeadline,
    DateOnly? NextPaymentDueDate,
    decimal? MinimumPaymentAmount,
    bool? IsOverdue,
    bool NeedsAlert,
    decimal? ManualCreditLimit,
    DateOnly? ManualNextPaymentDueDate,
    DateOnly? LastAlertSentDate,
    string? Nickname,
    string? Owner = null
);

public record UpdateCardRequest(
    int? StatementClosingDay,
    decimal? TargetUtilizationPercent,
    decimal? ManualCreditLimit,
    DateOnly? ManualNextPaymentDueDate,
    string? Nickname = null,
    string? Owner = null);

public record CardHistoryPointDto(DateOnly Date, decimal Balance, decimal? Limit, decimal? UtilizationPercent);

public record BestCardDto(
    string AccountId,
    string Name,
    string? Nickname,
    string? InstitutionName,
    DateOnly? NextClosingDate,
    int? DaysUntilClosing,
    decimal CurrentBalance,
    decimal CreditLimit,
    decimal? AvailableCredit,
    decimal? UtilizationPercent,
    string? ExclusionReason,
    string? Tier,
    decimal TargetPercent,
    bool OverTarget,
    string? Owner = null);

public record BestCardResponseDto(
    BestCardDto? Recommended,
    IReadOnlyList<BestCardDto> Ranking,
    IReadOnlyList<BestCardDto> Excluded);

public record PayoffPlanRequest(decimal AvailableAmount);

public record PayoffAllocationDto(
    string AccountId,
    string Name,
    string? Nickname,
    string? InstitutionName,
    decimal AmountToPay,
    decimal CurrentBalance,
    decimal? UtilizationBefore,
    decimal? UtilizationAfter,
    decimal TargetPercent,
    DateOnly? PaymentDeadline,
    int? DaysUntilPaymentDeadline,
    string Reason);

public record PayoffPlanResponseDto(
    decimal AvailableAmount,
    decimal AllocatedTotal,
    decimal RemainingUnallocated,
    IReadOnlyList<PayoffAllocationDto> Allocations);
