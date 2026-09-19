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
public class DashboardController : ControllerBase
{
    private readonly Target30DbContext _db;

    public DashboardController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // Gasto por categoria esse mês vs. mesmo período do mês passado (só até o mesmo dia, pra
    // não comparar um mês incompleto com um mês inteiro).
    [HttpGet("monthly-comparison")]
    public async Task<IActionResult> GetMonthlyComparison()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (currentMonth, previousMonth, _) = await LoadMonthlySpendAsync(today);

        var categories = currentMonth.Select(c => c.Category)
            .Union(previousMonth.Select(c => c.Category))
            .Distinct();

        var rows = categories
            .Select(cat =>
            {
                var current = currentMonth.FirstOrDefault(c => c.Category == cat)?.Total ?? 0m;
                var previous = previousMonth.FirstOrDefault(c => c.Category == cat)?.Total ?? 0m;
                decimal? changePercent = previous > 0 ? Math.Round((current - previous) / previous * 100, 1) : null;
                return new MonthlyComparisonRowDto(cat!, current, previous, changePercent);
            })
            .OrderByDescending(r => r.CurrentMonthTotal)
            .ToList();

        return Ok(rows);
    }

    // Selo único (0-100) resumindo utilização de cartão + tendência de gasto — ver
    // HealthScoreCalculator pra fórmula.
    [HttpGet("health-score")]
    public async Task<IActionResult> GetHealthScore()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == CurrentUserId)
            ?? new UserSettings { UserId = CurrentUserId };

        var cards = await _db.PlaidAccounts.Where(a => a.UserId == CurrentUserId && a.Type == "Credit").ToListAsync();
        var projections = cards.Select(c => CardMath.Compute(c, settings.GlobalTargetUtilizationPercent, today)).ToList();

        var (_, _, spend) = await LoadMonthlySpendAsync(today);

        var result = HealthScoreCalculator.Compute(projections, spend.Current, spend.Previous);
        return Ok(result);
    }

    private async Task<(
        List<CategoryTotalRow> CurrentMonth,
        List<CategoryTotalRow> PreviousMonth,
        (decimal Current, decimal Previous) Spend)> LoadMonthlySpendAsync(DateOnly today)
    {
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var firstOfLastMonth = firstOfThisMonth.AddMonths(-1);
        var lastOfLastMonthComparable = DateMath.BuildClamped(firstOfLastMonth.Year, firstOfLastMonth.Month, today.Day);

        var currentMonth = await _db.PlaidTransactions
            .Where(t => t.UserId == CurrentUserId && t.Amount > 0 && t.Date >= firstOfThisMonth && t.Date <= today)
            .GroupBy(t => (t.UserCategory ?? t.Category) ?? "OUTROS")
            .Select(g => new CategoryTotalRow(g.Key, g.Sum(t => t.Amount)))
            .ToListAsync();

        var previousMonth = await _db.PlaidTransactions
            .Where(t => t.UserId == CurrentUserId && t.Amount > 0
                && t.Date >= firstOfLastMonth && t.Date <= lastOfLastMonthComparable)
            .GroupBy(t => (t.UserCategory ?? t.Category) ?? "OUTROS")
            .Select(g => new CategoryTotalRow(g.Key, g.Sum(t => t.Amount)))
            .ToListAsync();

        var currentTotal = currentMonth.Sum(c => c.Total);
        var previousTotal = previousMonth.Sum(c => c.Total);

        return (currentMonth, previousMonth, (currentTotal, previousTotal));
    }

    private record CategoryTotalRow(string Category, decimal Total);
}

public record MonthlyComparisonRowDto(
    string Category, decimal CurrentMonthTotal, decimal PreviousMonthTotal, decimal? ChangePercent);
