using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Controllers;

// Teto de gasto mensal por categoria — separado da meta de utilização de cartão (que é sobre
// saldo/limite). Aqui é sobre soma de despesas na categoria dentro do mês corrente.
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BudgetsController : ControllerBase
{
    private readonly Target30DbContext _db;

    public BudgetsController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> GetBudgets()
    {
        var budgets = await _db.CategoryBudgets.Where(b => b.UserId == CurrentUserId).ToListAsync();
        var spend = await CurrentMonthSpendByCategoryAsync();

        var dtos = budgets
            .Select(b => ToDto(b, spend.GetValueOrDefault(b.Category, 0m)))
            .OrderByDescending(d => d.PercentUsed ?? -1)
            .ToList();

        return Ok(dtos);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBudget([FromBody] BudgetRequest request)
    {
        var alreadyExists = await _db.CategoryBudgets
            .AnyAsync(b => b.UserId == CurrentUserId && b.Category == request.Category);
        if (alreadyExists)
            return Conflict();

        var budget = new CategoryBudget
        {
            UserId = CurrentUserId,
            Category = request.Category,
            MonthlyLimit = Math.Max(0, request.MonthlyLimit),
        };
        _db.CategoryBudgets.Add(budget);
        await _db.SaveChangesAsync();

        var spend = await CurrentMonthSpendByCategoryAsync();
        return Ok(ToDto(budget, spend.GetValueOrDefault(budget.Category, 0m)));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBudget(int id, [FromBody] BudgetRequest request)
    {
        var budget = await _db.CategoryBudgets.FirstOrDefaultAsync(b => b.Id == id && b.UserId == CurrentUserId);
        if (budget is null)
            return NotFound();

        budget.MonthlyLimit = Math.Max(0, request.MonthlyLimit);
        await _db.SaveChangesAsync();

        var spend = await CurrentMonthSpendByCategoryAsync();
        return Ok(ToDto(budget, spend.GetValueOrDefault(budget.Category, 0m)));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBudget(int id)
    {
        var budget = await _db.CategoryBudgets.FirstOrDefaultAsync(b => b.Id == id && b.UserId == CurrentUserId);
        if (budget is null)
            return NotFound();

        _db.CategoryBudgets.Remove(budget);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<Dictionary<string, decimal>> CurrentMonthSpendByCategoryAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);

        var rows = await _db.PlaidTransactions
            .Where(t => t.UserId == CurrentUserId && t.Amount > 0 && t.Date >= firstOfMonth && t.Date <= today)
            .GroupBy(t => (t.UserCategory ?? t.Category) ?? "OUTROS")
            .Select(g => new { Category = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync();

        return rows.ToDictionary(r => r.Category, r => r.Total);
    }

    private static BudgetDto ToDto(CategoryBudget b, decimal currentSpend) => new(
        b.Id,
        b.Category,
        b.MonthlyLimit,
        currentSpend,
        b.MonthlyLimit > 0 ? Math.Round(currentSpend / b.MonthlyLimit * 100, 1) : null);
}

public record BudgetDto(int Id, string Category, decimal MonthlyLimit, decimal CurrentSpend, decimal? PercentUsed);

public record BudgetRequest(string Category, decimal MonthlyLimit);
