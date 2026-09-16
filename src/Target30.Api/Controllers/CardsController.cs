using System.Security.Claims;
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
                var targetPercent = a.TargetUtilizationPercent ?? settings.GlobalTargetUtilizationPercent;
                var limit = a.CreditLimit ?? 0m;
                var balance = a.CurrentBalance ?? 0m;
                var utilizationPercent = limit > 0 ? Math.Round(balance / limit * 100, 1) : (decimal?)null;
                var targetBalance = limit * (targetPercent / 100m);
                var amountToPay = Math.Max(0, Math.Round(balance - targetBalance, 2));
                var nextClosingDate = ComputeNextClosingDate(a.StatementClosingDay, today);
                // Bancos não processam pagamento em fim de semana/feriado — se o fechamento cair
                // num desses dias, o prazo real pra pagar é o último dia útil anterior.
                var paymentDeadline = nextClosingDate is not null
                    ? UsBusinessDays.PreviousOrSameBusinessDay(nextClosingDate.Value)
                    : (DateOnly?)null;
                var daysUntilDeadline = paymentDeadline is not null
                    ? paymentDeadline.Value.DayNumber - today.DayNumber
                    : (int?)null;
                var needsAlert = daysUntilDeadline is not null
                    && daysUntilDeadline <= settings.NotifyDaysBeforeClosing
                    && amountToPay > 0;

                return new CardDto(
                    a.AccountId,
                    a.Name,
                    a.OfficialName,
                    a.InstitutionName,
                    balance,
                    limit,
                    a.IsoCurrencyCode,
                    utilizationPercent,
                    targetPercent,
                    a.TargetUtilizationPercent is not null,
                    amountToPay,
                    a.StatementClosingDay,
                    nextClosingDate,
                    paymentDeadline,
                    daysUntilDeadline,
                    a.NextPaymentDueDate,
                    a.MinimumPaymentAmount,
                    a.IsOverdue,
                    needsAlert);
            })
            .OrderBy(c => c.DaysUntilPaymentDeadline ?? int.MaxValue)
            .ToList();

        return Ok(cards);
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

        await _db.SaveChangesAsync();
        return NoContent();
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

    private static DateOnly BuildClamped(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    private static DateOnly? ComputeNextClosingDate(int? closingDay, DateOnly today)
    {
        if (closingDay is null)
            return null;

        var candidate = BuildClamped(today.Year, today.Month, closingDay.Value);
        if (candidate <= today)
        {
            var next = today.AddMonths(1);
            candidate = BuildClamped(next.Year, next.Month, closingDay.Value);
        }
        return candidate;
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
    bool NeedsAlert
);

public record UpdateCardRequest(int? StatementClosingDay, decimal? TargetUtilizationPercent);
