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
public class IncomeController : ControllerBase
{
    private readonly Target30DbContext _db;

    public IncomeController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> GetIncomes()
    {
        var incomes = await _db.RecurringIncomes
            .Where(i => i.UserId == CurrentUserId && i.IsActive)
            .OrderBy(i => i.Description)
            .ToListAsync();
        return Ok(incomes.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> CreateIncome([FromBody] IncomeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Description) || request.Amount <= 0 || !IncomeFrequency.IsValid(request.Frequency))
            return BadRequest();

        var income = new RecurringIncome
        {
            UserId = CurrentUserId,
            Description = request.Description.Trim(),
            Amount = request.Amount,
            Frequency = request.Frequency,
            AnchorDate = request.AnchorDate,
            IsActive = true,
        };
        _db.RecurringIncomes.Add(income);
        await _db.SaveChangesAsync();
        return Ok(ToDto(income));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteIncome(int id)
    {
        var income = await _db.RecurringIncomes.FirstOrDefaultAsync(i => i.Id == id && i.UserId == CurrentUserId);
        if (income is null)
            return NotFound();

        _db.RecurringIncomes.Remove(income);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // Sugestões a partir do histórico da conta corrente (não inclui as já cadastradas).
    [HttpGet("detected")]
    public async Task<IActionResult> GetDetected()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var existing = await _db.RecurringIncomes
            .Where(i => i.UserId == CurrentUserId && i.IsActive)
            .Select(i => i.Description)
            .ToListAsync();

        var depositoryIds = _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Type == "Depository")
            .Select(a => a.AccountId);
        var inflows = await _db.PlaidTransactions
            .ExcludingInternalTransfers()
            .Where(t => t.UserId == CurrentUserId && t.Amount < 0 && !t.Pending && depositoryIds.Contains(t.AccountId))
            .ToListAsync();

        var detected = inflows
            .GroupBy(t => IncomeDetector.Normalize(t.MerchantName ?? t.Name), StringComparer.OrdinalIgnoreCase)
            .Select(g => IncomeDetector.Detect(g.Key, g.ToList(), today))
            .Where(d => d is not null && !existing.Contains(d.Description, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(d => d!.Amount)
            .ToList();

        return Ok(detected);
    }

    private static IncomeDto ToDto(RecurringIncome i) => new(i.Id, i.Description, i.Amount, i.Frequency, i.AnchorDate);
}

public record IncomeDto(int Id, string Description, decimal Amount, string Frequency, DateOnly AnchorDate);

public record IncomeRequest(string Description, decimal Amount, string Frequency, DateOnly AnchorDate);
