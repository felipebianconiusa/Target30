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
                    a.NextPaymentDueDate,
                    a.MinimumPaymentAmount,
                    a.IsOverdue,
                    needsAlert);
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
        var accounts = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Type == "Credit")
            .OrderBy(a => a.InstitutionName)
            .ThenBy(a => a.Name)
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", new[]
        {
            "Cartao", "Instituicao", "Saldo Atual", "Limite", "Utilizacao %", "Meta %",
            "Valor a Pagar", "Data de Fechamento", "Prazo de Pagamento (dia util)",
            "Data de Vencimento", "Pagamento Minimo", "Em Atraso",
        }.Select(CsvField)));

        foreach (var a in accounts)
        {
            var p = CardMath.Compute(a, settings.GlobalTargetUtilizationPercent, today);
            csv.AppendLine(string.Join(",", new[]
            {
                CsvField(a.Name),
                CsvField(a.InstitutionName ?? ""),
                CsvField(p.Balance.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(p.Limit.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(p.UtilizationPercent?.ToString("F1", CultureInfo.InvariantCulture) ?? ""),
                CsvField(p.TargetPercent.ToString("F0", CultureInfo.InvariantCulture)),
                CsvField(p.AmountToPay.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(p.NextClosingDate?.ToString("yyyy-MM-dd") ?? ""),
                CsvField(p.PaymentDeadline?.ToString("yyyy-MM-dd") ?? ""),
                CsvField(a.NextPaymentDueDate?.ToString("yyyy-MM-dd") ?? ""),
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
