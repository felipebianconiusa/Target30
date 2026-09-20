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
public class RewardsController : ControllerBase
{
    private readonly Target30DbContext _db;

    public RewardsController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> GetRates()
    {
        var rates = await _db.CardRewardRates.Where(r => r.UserId == CurrentUserId).ToListAsync();
        var result = rates
            .GroupBy(r => r.AccountId)
            .Select(g => new CardRewardsDto(
                g.Key,
                g.OrderBy(r => r.Category == CardRewardRate.BaseCategory ? "" : r.Category)
                    .Select(r => new RewardRateDto(r.Category, r.RatePercent))
                    .ToList()))
            .ToList();
        return Ok(result);
    }

    // Substitui todas as taxas do cartão pelas enviadas (lista vazia = limpa).
    [HttpPut("{accountId}")]
    public async Task<IActionResult> SetRates(string accountId, [FromBody] RewardsRequest request)
    {
        var owned = await _db.PlaidAccounts.AnyAsync(a => a.AccountId == accountId && a.UserId == CurrentUserId && a.Type == "Credit");
        if (!owned)
            return NotFound();

        var rates = request.Rates ?? [];
        if (rates.Any(r => string.IsNullOrWhiteSpace(r.Category) || r.Category.Length > 60 || r.RatePercent is < 0 or > 100))
            return BadRequest();

        // Categoria repetida: vale a última.
        var cleaned = rates
            .GroupBy(r => r.Category.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new RewardRateDto(g.Key, Math.Round(g.Last().RatePercent, 2)))
            .ToList();

        var existing = _db.CardRewardRates.Where(r => r.UserId == CurrentUserId && r.AccountId == accountId);
        _db.CardRewardRates.RemoveRange(existing);
        foreach (var r in cleaned)
            _db.CardRewardRates.Add(new CardRewardRate
            {
                UserId = CurrentUserId, AccountId = accountId, Category = r.Category, RatePercent = r.RatePercent,
            });

        await _db.SaveChangesAsync();
        return Ok(new CardRewardsDto(accountId, cleaned));
    }

    // Ranking dos cartões pra uma compra dessa categoria (maior recompensa primeiro).
    [HttpGet("best-for")]
    public async Task<IActionResult> BestFor([FromQuery] string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return BadRequest();

        var settings = await _db.UserSettings.FirstOrDefaultAsync(s => s.UserId == CurrentUserId)
            ?? new UserSettings { UserId = CurrentUserId };
        var accounts = await _db.PlaidAccounts
            .Where(a => a.UserId == CurrentUserId && a.Type == "Credit")
            .ToListAsync();
        var rates = await _db.CardRewardRates.Where(r => r.UserId == CurrentUserId).ToListAsync();
        var ratesByAccount = rates
            .GroupBy(r => r.AccountId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, decimal>)g.ToDictionary(r => r.Category, r => r.RatePercent));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (eligible, excluded) = BestCardPicker.Rank(
            accounts.Select(a => (Account: a, Projection: CardMath.Compute(a, settings.GlobalTargetUtilizationPercent, today))),
            today);

        var ranking = RewardPicker.Rank(eligible.Concat(excluded), ratesByAccount, category.Trim());
        var list = ranking.Select((r, i) => new RewardRankingDto(
            r.Card.Account.AccountId,
            r.Card.Account.Name,
            r.Card.Account.Nickname,
            r.Card.Account.InstitutionName,
            r.Card.Account.Owner,
            r.RatePercent,
            r.Card.Tier switch
            {
                CardTier.Recommended => "recommended",
                CardTier.Alternative => "alternative",
                CardTier.Caution => "caution",
                _ => null,
            },
            r.Card.DaysUntilClosing,
            r.Card.Projection.NextClosingDate,
            r.Card.AvailableCredit,
            i == 0 && !r.Caution)).ToList();

        return Ok(list);
    }
}

public record RewardRateDto(string Category, decimal RatePercent);

public record CardRewardsDto(string AccountId, IReadOnlyList<RewardRateDto> Rates);

public record RewardsRequest(List<RewardRateDto>? Rates);

public record RewardRankingDto(
    string AccountId,
    string Name,
    string? Nickname,
    string? InstitutionName,
    string? Owner,
    decimal RatePercent,
    string? Tier,
    int? DaysUntilClosing,
    DateOnly? NextClosingDate,
    decimal? AvailableCredit,
    bool IsBest);
