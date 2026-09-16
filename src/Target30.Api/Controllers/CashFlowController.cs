using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;

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

        var cards = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Type == "Credit")
            .ToListAsync();
        foreach (var card in cards)
        {
            var closingDate = ComputeNextClosingDate(card.StatementClosingDay, today);
            if (closingDate is not null)
            {
                var deadline = UsBusinessDays.PreviousOrSameBusinessDay(closingDate.Value);
                if (deadline > today && deadline <= endDate)
                    entries.Add((deadline, $"{card.Name} - Fechamento", 0m, "Pending", 1));
            }

            if (card.NextPaymentDueDate is { } dueDate && dueDate > today && dueDate <= endDate && card.LastStatementBalance is { } statementBalance)
                entries.Add((dueDate, $"{card.Name} - Fatura", -statementBalance, "Pending", 1));
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

        return Ok(new CashFlowResponseDto(startingBalance, currentBalance, rows));
    }

    private static DateOnly? ComputeNextClosingDate(int? closingDay, DateOnly today)
    {
        if (closingDay is null)
            return null;

        var candidate = DateMath.BuildClamped(today.Year, today.Month, closingDay.Value);
        if (candidate <= today)
        {
            var next = today.AddMonths(1);
            candidate = DateMath.BuildClamped(next.Year, next.Month, closingDay.Value);
        }
        return candidate;
    }
}

public record CashFlowEntryDto(DateOnly Date, string Description, decimal Amount, decimal Balance, string Status);

public record CashFlowResponseDto(decimal StartingBalance, decimal CurrentBalance, IReadOnlyList<CashFlowEntryDto> Entries);
