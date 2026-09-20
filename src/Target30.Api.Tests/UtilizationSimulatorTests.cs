using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class UtilizationSimulatorTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    private static (PlaidAccount, CardProjection) Card(string name, decimal balance, decimal limit, int? daysToClose = 10)
    {
        var closing = daysToClose is null ? (DateOnly?)null : Today.AddDays(daysToClose.Value);
        var account = new PlaidAccount { UserId = "u", ItemId = "i", AccountId = name, Name = name, Type = "Credit" };
        return (account, new CardProjection(balance, limit, null, 30m, 0m, closing, closing, daysToClose));
    }

    [Fact]
    public void Computes_how_much_to_pay_to_reach_the_target_percent_per_card_and_overall()
    {
        var sim = UtilizationSimulator.Simulate([Card("a", 500m, 1000m), Card("b", 200m, 4000m)], 30m);

        var a = sim.Cards.Single(c => c.Account.Name == "a");
        Assert.Equal((500m, 200m, 50m, 30m), (a.Balance, a.ToPay, a.UtilizationBefore, a.UtilizationAfter));
        Assert.Equal(0m, sim.Cards.Single(c => c.Account.Name == "b").ToPay);
        Assert.Equal(200m, sim.TotalToPay);
        Assert.Equal(14m, sim.OverallBefore); // 700 / 5000
        Assert.Equal(10m, sim.OverallAfter);  // 500 / 5000
    }

    [Fact]
    public void A_lower_band_needs_a_bigger_payment()
    {
        var thirty = UtilizationSimulator.Simulate([Card("a", 500m, 1000m)], 30m).TotalToPay;
        var nine = UtilizationSimulator.Simulate([Card("a", 500m, 1000m)], 9m).TotalToPay;

        Assert.Equal(200m, thirty);
        Assert.Equal(410m, nine);
    }

    [Fact]
    public void Rounds_the_target_balance_down_so_the_result_never_exceeds_the_band()
    {
        // 9% de 333 = 29,97 -> alvo 29,97 (arredonda pra baixo em centavos); saldo 100 -> paga 70,03.
        var sim = UtilizationSimulator.Simulate([Card("a", 100m, 333m)], 9m);

        Assert.Equal(70.03m, sim.Cards[0].ToPay);
        Assert.True(sim.Cards[0].UtilizationAfter <= 9m);
    }

    [Fact]
    public void Cards_without_a_known_limit_are_listed_apart_and_left_out_of_the_math()
    {
        var sim = UtilizationSimulator.Simulate([Card("known", 500m, 1000m), Card("no-limit", 900m, 0m)], 30m);

        Assert.Equal("no-limit", Assert.Single(sim.SkippedWithoutLimit).Name);
        Assert.Equal("known", Assert.Single(sim.Cards).Account.Name);
        Assert.Equal(50m, sim.OverallBefore);
    }

    [Fact]
    public void The_card_closing_first_comes_first()
    {
        var sim = UtilizationSimulator.Simulate([Card("late", 500m, 1000m, 20), Card("soon", 500m, 1000m, 3)], 30m);

        Assert.Equal(["soon", "late"], sim.Cards.Select(c => c.Account.Name));
    }

    [Fact]
    public void A_negative_balance_never_asks_for_a_payment_and_overall_is_null_without_limits()
    {
        Assert.Equal(0m, UtilizationSimulator.Simulate([Card("a", -20m, 1000m)], 30m).TotalToPay);
        Assert.Null(UtilizationSimulator.Simulate([Card("a", 100m, 0m)], 30m).OverallBefore);
    }
}

public class UtilizationPlanEndpointTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UtilizationPlanEndpointTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_plan_uses_only_the_current_users_credit_cards()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "mine", Name = "Mine", Type = "Credit",
                CurrentBalance = 500m, CreditLimit = 1000m, StatementClosingDay = 10,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "chk", Name = "Checking", Type = "Depository", CurrentBalance = 9999m,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = "someone-else", ItemId = "i", AccountId = "theirs", Name = "Theirs", Type = "Credit", CurrentBalance = 900m, CreditLimit = 1000m,
            });
        });

        var plan = await _client.GetFromJsonAsync<UtilizationPlanDto>("/api/cards/utilization-plan?target=10", JsonDefaults.Options);

        var card = Assert.Single(plan!.Cards);
        Assert.Equal("Mine", card.Name);
        Assert.Equal(400m, card.ToPay);
        Assert.Equal(400m, plan.TotalToPay);
        Assert.Equal(10m, plan.TargetPercent);
    }

    [Fact]
    public async Task The_target_defaults_to_thirty_and_is_clamped()
    {
        var def = await _client.GetFromJsonAsync<UtilizationPlanDto>("/api/cards/utilization-plan", JsonDefaults.Options);
        var over = await _client.GetFromJsonAsync<UtilizationPlanDto>("/api/cards/utilization-plan?target=500", JsonDefaults.Options);

        Assert.Equal(30m, def!.TargetPercent);
        Assert.Equal(100m, over!.TargetPercent);
    }
}
