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
public class BillsController : ControllerBase
{
    private readonly Target30DbContext _db;

    public BillsController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> GetBills()
    {
        var bills = await _db.RecurringBills
            .Where(b => b.UserId == CurrentUserId && b.IsActive)
            .OrderBy(b => b.DayOfMonth)
            .ToListAsync();

        return Ok(bills.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> CreateBill([FromBody] BillRequest request)
    {
        var bill = new RecurringBill
        {
            UserId = CurrentUserId,
            Description = request.Description,
            Amount = request.Amount,
            DayOfMonth = Math.Clamp(request.DayOfMonth, 1, 31),
            IsActive = true,
        };
        _db.RecurringBills.Add(bill);
        await _db.SaveChangesAsync();
        return Ok(ToDto(bill));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBill(int id, [FromBody] BillRequest request)
    {
        var bill = await _db.RecurringBills.FirstOrDefaultAsync(b => b.Id == id && b.UserId == CurrentUserId);
        if (bill is null)
            return NotFound();

        bill.Description = request.Description;
        bill.Amount = request.Amount;
        bill.DayOfMonth = Math.Clamp(request.DayOfMonth, 1, 31);
        await _db.SaveChangesAsync();
        return Ok(ToDto(bill));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBill(int id)
    {
        var bill = await _db.RecurringBills.FirstOrDefaultAsync(b => b.Id == id && b.UserId == CurrentUserId);
        if (bill is null)
            return NotFound();

        _db.RecurringBills.Remove(bill);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Heurística simples pra sugerir assinaturas/cobranças recorrentes ainda não cadastradas:
    // mesmo estabelecimento, valor consistente (±15%, mín. ±$2) e intervalo entre compras
    // sempre parecido com um mês (21-40 dias). Não é perfeito — é só um ponto de partida.
    [HttpGet("detected-subscriptions")]
    public async Task<IActionResult> GetDetectedSubscriptions()
    {
        var existingDescriptions = await _db.RecurringBills
            .Where(b => b.UserId == CurrentUserId && b.IsActive)
            .Select(b => b.Description.ToLower())
            .ToListAsync();

        var transactions = await _db.PlaidTransactions
            .Where(t => t.UserId == CurrentUserId && t.Amount > 0 && !t.Pending)
            .ToListAsync();

        var candidates = transactions
            .GroupBy(t => (t.MerchantName ?? t.Name).Trim().ToLowerInvariant())
            .Select(g => DetectSubscription(g.OrderBy(t => t.Date).ToList()))
            .Where(c => c is not null && !existingDescriptions.Contains(c!.MerchantName.ToLower()))
            .OrderByDescending(c => c!.LastDate)
            .ToList();

        return Ok(candidates);
    }

    private static DetectedSubscriptionDto? DetectSubscription(List<PlaidTransaction> txs)
    {
        if (txs.Count < 2)
            return null;

        var avgAmount = txs.Average(t => t.Amount);
        var tolerance = Math.Max(2m, avgAmount * 0.15m);
        if (txs.Any(t => Math.Abs(t.Amount - avgAmount) > tolerance))
            return null;

        var gaps = new List<int>();
        for (var i = 1; i < txs.Count; i++)
            gaps.Add(txs[i].Date.DayNumber - txs[i - 1].Date.DayNumber);

        if (gaps.Any(g => g is < 21 or > 40))
            return null;

        var last = txs[^1];
        var displayName = last.MerchantName ?? last.Name;
        return new DetectedSubscriptionDto(displayName, Math.Round(avgAmount, 2), last.Date.Day, txs.Count, last.Date);
    }

    private static BillDto ToDto(RecurringBill b) => new(b.Id, b.Description, b.Amount, b.DayOfMonth);
}

public record BillDto(int Id, string Description, decimal Amount, int DayOfMonth);

public record BillRequest(string Description, decimal Amount, int DayOfMonth);

public record DetectedSubscriptionDto(
    string MerchantName,
    decimal AverageAmount,
    int SuggestedDayOfMonth,
    int Occurrences,
    DateOnly LastDate);
