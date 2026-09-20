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

    // Sugere assinaturas/cobranças recorrentes ainda não cadastradas (via SubscriptionDetector)
    // e também resurge as já cadastradas cujo valor observado no histórico mudou — nesse caso
    // com IsPriceChange=true, pra oferecer "Atualizar" em vez de "Adicionar".
    [HttpGet("detected-subscriptions")]
    public async Task<IActionResult> GetDetectedSubscriptions()
    {
        var existingBills = await _db.RecurringBills
            .Where(b => b.UserId == CurrentUserId && b.IsActive)
            .ToListAsync();

        var transactions = await _db.PlaidTransactions
            .ExcludingInternalTransfers()
            .Where(t => t.UserId == CurrentUserId && t.Amount > 0 && !t.Pending)
            .ToListAsync();

        var candidates = transactions
            .GroupBy(t => (t.MerchantName ?? t.Name).Trim().ToLowerInvariant())
            .Select(g => SubscriptionDetector.Detect(g.ToList()))
            .Where(c => c is not null)
            .Select(c => ToSuggestion(c!, existingBills))
            .Where(dto => dto is not null)
            .OrderByDescending(dto => dto!.LastDate)
            .ToList();

        return Ok(candidates);
    }

    private static DetectedSubscriptionDto? ToSuggestion(DetectedCharge charge, List<RecurringBill> existingBills)
    {
        var existing = existingBills.FirstOrDefault(
            b => b.Description.Equals(charge.MerchantName, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
            return new DetectedSubscriptionDto(
                charge.MerchantName, charge.AverageAmount, charge.SuggestedDayOfMonth, charge.Occurrences,
                charge.LastDate, false, null, null);

        var tolerance = Math.Max(1m, existing.Amount * 0.05m);
        var priceChanged = Math.Abs(existing.Amount - charge.AverageAmount) > tolerance;
        if (!priceChanged)
            return null; // já cadastrada com o valor certo — nada a sugerir

        return new DetectedSubscriptionDto(
            charge.MerchantName, charge.AverageAmount, charge.SuggestedDayOfMonth, charge.Occurrences,
            charge.LastDate, true, existing.Amount, existing.Id);
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
    DateOnly LastDate,
    bool IsPriceChange,
    decimal? PreviousAmount,
    int? ExistingBillId);
