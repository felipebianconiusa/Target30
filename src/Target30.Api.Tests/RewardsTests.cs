using System.Net;
using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class RewardPickerTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static (PlaidAccount, CardProjection) Card(string name, DateOnly? closing, decimal balance = 100m, decimal limit = 1000m)
    {
        var account = new PlaidAccount { UserId = "u", ItemId = "i", AccountId = name, Name = name, Type = "Credit" };
        decimal? utilization = limit > 0 ? Math.Round(balance / limit * 100, 1) : null;
        return (account, new CardProjection(balance, limit, utilization, 30m, 0m, closing, closing, null));
    }

    private static List<RewardRanking> Rank(
        (PlaidAccount, CardProjection)[] cards, Dictionary<string, Dictionary<string, decimal>> rates, string category)
    {
        var (eligible, excluded) = BestCardPicker.Rank(cards, Today);
        var byAccount = rates.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, decimal>)kv.Value);
        return RewardPicker.Rank(eligible.Concat(excluded), byAccount, category);
    }

    [Fact]
    public void RateFor_prefers_the_category_rate_then_the_base_rate_then_zero()
    {
        var rates = new Dictionary<string, decimal> { ["FOOD_AND_DRINK"] = 3m, ["BASE"] = 1m };

        Assert.Equal(3m, RewardPicker.RateFor(rates, "FOOD_AND_DRINK"));
        Assert.Equal(1m, RewardPicker.RateFor(rates, "TRAVEL"));
        Assert.Equal(0m, RewardPicker.RateFor(new Dictionary<string, decimal>(), "TRAVEL"));
        Assert.Equal(0m, RewardPicker.RateFor(null, "TRAVEL"));
    }

    [Fact]
    public void Ranks_by_the_highest_reward_for_the_category()
    {
        var ranking = Rank(
            [Card("flat", Today.AddDays(20)), Card("dining", Today.AddDays(18))],
            new() { ["flat"] = new() { ["BASE"] = 2m }, ["dining"] = new() { ["FOOD_AND_DRINK"] = 3m, ["BASE"] = 1m } },
            "FOOD_AND_DRINK");

        Assert.Equal(["dining", "flat"], ranking.Select(r => r.Card.Account.Name));
        Assert.Equal(3m, ranking[0].RatePercent);
    }

    [Fact]
    public void Uses_the_base_rate_when_the_category_has_no_specific_rate()
    {
        var ranking = Rank(
            [Card("flat", Today.AddDays(20)), Card("dining", Today.AddDays(18))],
            new() { ["flat"] = new() { ["BASE"] = 2m }, ["dining"] = new() { ["FOOD_AND_DRINK"] = 3m, ["BASE"] = 1m } },
            "TRAVEL");

        Assert.Equal("flat", ranking[0].Card.Account.Name);
    }

    [Fact]
    public void A_tie_goes_to_the_card_that_takes_longer_to_close()
    {
        var ranking = Rank(
            [Card("soon", Today.AddDays(10)), Card("late", Today.AddDays(25))],
            new() { ["soon"] = new() { ["BASE"] = 2m }, ["late"] = new() { ["BASE"] = 2m } },
            "TRAVEL");

        Assert.Equal(["late", "soon"], ranking.Select(r => r.Card.Account.Name));
    }

    [Fact]
    public void A_card_in_the_caution_group_only_wins_when_nothing_else_is_left()
    {
        // "hot" rende 5%, mas fecha em 2 dias (cautela) — o de 1% dentro da meta vem antes.
        var ranking = Rank(
            [Card("hot", Today.AddDays(2)), Card("calm", Today.AddDays(20))],
            new() { ["hot"] = new() { ["BASE"] = 5m }, ["calm"] = new() { ["BASE"] = 1m } },
            "TRAVEL");

        Assert.Equal(["calm", "hot"], ranking.Select(r => r.Card.Account.Name));
        Assert.True(ranking[1].Caution);
    }

    [Fact]
    public void A_maxed_out_card_is_left_out_but_one_without_a_closing_day_stays_in()
    {
        var ranking = Rank(
            [Card("maxed", Today.AddDays(20), balance: 1000m), Card("no-day", null), Card("ok", Today.AddDays(20))],
            new() { ["maxed"] = new() { ["BASE"] = 9m }, ["no-day"] = new() { ["BASE"] = 4m }, ["ok"] = new() { ["BASE"] = 1m } },
            "TRAVEL");

        Assert.DoesNotContain(ranking, r => r.Card.Account.Name == "maxed");
        Assert.Equal(["no-day", "ok"], ranking.Select(r => r.Card.Account.Name));
        Assert.True(ranking[0].NoClosingDay);
    }
}

public class RewardsEndpointTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public RewardsEndpointTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task SeedTwoCards()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "savor", Name = "Savor", Type = "Credit",
                CurrentBalance = 50m, CreditLimit = 1000m, StatementClosingDay = today.AddDays(20).Day,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "flat", Name = "Flat", Type = "Credit",
                CurrentBalance = 50m, CreditLimit = 1000m, StatementClosingDay = today.AddDays(20).Day,
            });
        });
    }

    private static RewardRateDto R(string category, decimal rate) => new(category, rate);

    private Task<HttpResponseMessage> Put(string accountId, params RewardRateDto[] rates) =>
        _client.PutAsJsonAsync($"/api/rewards/{accountId}", new RewardsRequest(rates.ToList()), JsonDefaults.Options);

    [Fact]
    public async Task Rates_are_saved_replaced_and_listed_per_card()
    {
        await SeedTwoCards();

        await Put("savor", R("BASE", 1m), R("FOOD_AND_DRINK", 3m));
        await Put("savor", R("BASE", 1.5m));

        var all = await _client.GetFromJsonAsync<List<CardRewardsDto>>("/api/rewards", JsonDefaults.Options);
        var savor = Assert.Single(all!);
        var rate = Assert.Single(savor.Rates);
        Assert.Equal(("BASE", 1.5m), (rate.Category, rate.RatePercent));
    }

    [Fact]
    public async Task Rejects_out_of_range_rates_and_unknown_cards()
    {
        await SeedTwoCards();

        Assert.Equal(HttpStatusCode.BadRequest, (await Put("savor", R("BASE", 101m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put("savor", R("BASE", -1m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Put("savor", R(" ", 1m))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Put("nope", R("BASE", 1m))).StatusCode);
    }

    [Fact]
    public async Task Cannot_set_rates_on_someone_elses_card()
    {
        await _factory.SeedAsync(db => db.PlaidAccounts.Add(new PlaidAccount
        {
            UserId = "someone-else", ItemId = "i", AccountId = "theirs", Name = "Theirs", Type = "Credit",
        }));

        Assert.Equal(HttpStatusCode.NotFound, (await Put("theirs", R("BASE", 1m))).StatusCode);
    }

    [Fact]
    public async Task BestFor_puts_the_card_with_the_best_rate_for_the_category_first()
    {
        await SeedTwoCards();
        await Put("savor", R("FOOD_AND_DRINK", 3m), R("BASE", 1m));
        await Put("flat", R("BASE", 2m));

        var dining = await _client.GetFromJsonAsync<List<RewardRankingDto>>("/api/rewards/best-for?category=FOOD_AND_DRINK", JsonDefaults.Options);
        var travel = await _client.GetFromJsonAsync<List<RewardRankingDto>>("/api/rewards/best-for?category=TRAVEL", JsonDefaults.Options);

        Assert.Equal("Savor", dining![0].Name);
        Assert.True(dining[0].IsBest);
        Assert.Equal(3m, dining[0].RatePercent);
        Assert.Equal("Flat", travel![0].Name);
    }

    [Fact]
    public async Task BestFor_requires_a_category()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/rewards/best-for?category=")).StatusCode);
    }
}
