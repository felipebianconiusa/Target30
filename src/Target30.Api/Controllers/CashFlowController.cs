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
public class CashFlowController : ControllerBase
{
    private readonly Target30DbContext _db;

    public CashFlowController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Panorama estilo planilha: o que já aconteceu (Done/Pending do Plaid) + o que é esperado
    // (contas recorrentes + faturas de cartão a vencer), com saldo projetado dia a dia.
    // "Amount" aqui já vem com o sinal invertido do Plaid: positivo = entrada, negativo = saída
    // (assim bate com a leitura natural de uma planilha de fluxo de caixa).
    [HttpGet]
    public async Task<IActionResult> GetCashFlow([FromQuery] int pastDays = 30, [FromQuery] int futureDays = 45)
    {
        var (startingBalance, currentBalance, rows) = await BuildRowsAsync(pastDays, futureDays);
        return Ok(new CashFlowResponseDto(startingBalance, currentBalance, rows));
    }

    // Mesmo cálculo da tela de Cash Flow, em CSV — pra baixar, guardar ou comparar com a
    // planilha antiga.
    [HttpGet("report")]
    public async Task<IActionResult> DownloadReport([FromQuery] int pastDays = 30, [FromQuery] int futureDays = 45)
    {
        var (_, _, rows) = await BuildRowsAsync(pastDays, futureDays);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", new[] { "Data", "Descricao", "Valor", "Saldo", "Situacao" }.Select(CsvField)));
        foreach (var r in rows)
        {
            csv.AppendLine(string.Join(",", new[]
            {
                CsvField(r.Date.ToString("yyyy-MM-dd")),
                CsvField(r.Description),
                CsvField(r.Amount.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(r.Balance.ToString("F2", CultureInfo.InvariantCulture)),
                CsvField(r.Status),
            }));
        }

        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return File(bytes, "text/csv", $"target30-fluxo-caixa-{today:yyyy-MM-dd}.csv");
    }

    private static string CsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private async Task<(decimal StartingBalance, decimal CurrentBalance, List<CashFlowEntryDto> Rows)> BuildRowsAsync(
        int pastDays, int futureDays)
    {
        pastDays = Math.Clamp(pastDays, 0, 365);
        futureDays = Math.Clamp(futureDays, 0, 365);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = today.AddDays(-pastDays);
        var endDate = today.AddDays(futureDays);

        var currentBalance = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Type == "Depository")
            .SumAsync(a => (decimal?)a.CurrentBalance) ?? 0m;

        var entries = new List<(DateOnly Date, string Description, decimal Amount, string Status, int SortPriority)>();

        var transactions = await _db.PlaidTransactions
            .Where(t => t.UserId == CurrentUserId && t.Date >= startDate && t.Date <= today)
            .ToListAsync();
        foreach (var t in transactions)
            entries.Add((t.Date, t.MerchantName ?? t.Name, -t.Amount, t.Pending ? "Pending" : "Done", 0));

        var bills = await _db.RecurringBills
            .Where(b => b.UserId == CurrentUserId && b.IsActive)
            .ToListAsync();
        foreach (var bill in bills)
        {
            foreach (var date in DateMath.MonthlyOccurrences(bill.DayOfMonth, today, endDate))
                entries.Add((date, bill.Description, -bill.Amount, "Pending", 1));
        }

        var settings = await GetOrCreateSettingsAsync();
        var cards = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Type == "Credit")
            .ToListAsync();
        foreach (var card in cards)
        {
            var p = CardMath.Compute(card, settings.GlobalTargetUtilizationPercent, today);

            // Lançamento no fechamento: quanto pagar pra bater a meta de utilização — sempre
            // aparece (mesmo R$0, se já estiver dentro da meta) pra você saber a data mesmo
            // que não precise pagar nada. Reflete o saldo ATUAL do cartão, então atualiza a
            // cada gasto novo. Sem limite cadastrado ainda (p.UtilizationPercent null) não dá
            // pra calcular "quanto pagar pra bater a meta", então pulamos esse lançamento.
            if (p.UtilizationPercent is not null && p.PaymentDeadline is { } deadline && deadline > today && deadline <= endDate)
                entries.Add((deadline, $"{card.Name} - Fechamento", -p.AmountToPay, "Pending", 1));

            // Lançamento no vencimento: o saldo atual do cartão (o que vai virar fatura) —
            // também sempre aparece, mesmo R$0, pra marcar a data.
            if (card.EffectiveNextPaymentDueDate is { } dueDate && dueDate > today && dueDate <= endDate)
                entries.Add((dueDate, $"{card.Name} - Fatura", -p.Balance, "Pending", 1));
        }

        var ordered = entries.OrderBy(e => e.Date).ThenBy(e => e.SortPriority).ToList();

        // O saldo atual já reflete tudo que já aconteceu até hoje — pra desenhar a "trilha" a
        // partir do início da janela, caminhamos pra trás a partir dele.
        var netEffectUpToToday = ordered.Where(e => e.Date <= today).Sum(e => e.Amount);
        var runningBalance = currentBalance - netEffectUpToToday;
        var startingBalance = runningBalance;

        var rows = new List<CashFlowEntryDto>();
        foreach (var e in ordered)
        {
            runningBalance += e.Amount;
            rows.Add(new CashFlowEntryDto(e.Date, e.Description, e.Amount, runningBalance, e.Status));
        }

        return (startingBalance, currentBalance, rows);
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

public record CashFlowEntryDto(DateOnly Date, string Description, decimal Amount, decimal Balance, string Status);

public record CashFlowResponseDto(decimal StartingBalance, decimal CurrentBalance, IReadOnlyList<CashFlowEntryDto> Entries);
