using System.Net;
using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class CardsControllerTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CardsControllerTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetCards_only_returns_credit_accounts_belonging_to_the_current_user()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId,
                ItemId = "item-1",
                AccountId = "acc-mine",
                Name = "Mine",
                Type = "Credit",
                CurrentBalance = 300m,
                CreditLimit = 1000m,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = "someone-else",
                ItemId = "item-2",
                AccountId = "acc-not-mine",
                Name = "Not mine",
                Type = "Credit",
                CurrentBalance = 100m,
                CreditLimit = 1000m,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId,
                ItemId = "item-1",
                AccountId = "acc-checking",
                Name = "Checking, not a credit card",
                Type = "Depository",
                CurrentBalance = 5000m,
            });
        });

        var cards = await _client.GetFromJsonAsync<List<CardDto>>("/api/cards", JsonDefaults.Options);

        var card = Assert.Single(cards!);
        Assert.Equal("Mine", card.Name);
    }

    [Fact]
    public async Task GetCards_computes_utilization_and_amount_to_pay_using_the_global_target()
    {
        await _factory.SeedAsync(db =>
        {
            db.UserSettings.Add(new UserSettings
            {
                UserId = TestAuthHandler.TestUserId,
                GlobalTargetUtilizationPercent = 30m,
                NotifyDaysBeforeClosing = 3,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId,
                ItemId = "item-1",
                AccountId = "acc-1",
                Name = "Test Visa",
                Type = "Credit",
                CurrentBalance = 450m,
                CreditLimit = 1000m,
            });
        });

        var cards = await _client.GetFromJsonAsync<List<CardDto>>("/api/cards", JsonDefaults.Options);

        var card = Assert.Single(cards!);
        Assert.Equal(45m, card.UtilizationPercent);
        Assert.Equal(30m, card.TargetPercent);
        Assert.Equal(150m, card.AmountToPay); // 450 - (1000 * 30%)
    }

    [Fact]
    public async Task UpdateCard_persists_manual_override_fields()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId,
                ItemId = "item-1",
                AccountId = "acc-onepay",
                Name = "OnePay Cash Rewards",
                Type = "Credit",
                CurrentBalance = 72.14m,
                CreditLimit = null,
            });
        });

        var request = new UpdateCardRequest(
            StatementClosingDay: 15,
            TargetUtilizationPercent: 10m,
            ManualCreditLimit: 1000m,
            ManualNextPaymentDueDate: new DateOnly(2026, 10, 5));

        var putResponse = await _client.PutAsJsonAsync("/api/cards/acc-onepay", request, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);

        var cards = await _client.GetFromJsonAsync<List<CardDto>>("/api/cards", JsonDefaults.Options);
        var card = Assert.Single(cards!);

        Assert.Equal(1000m, card.ManualCreditLimit);
        Assert.Equal(new DateOnly(2026, 10, 5), card.ManualNextPaymentDueDate);
        Assert.Equal(new DateOnly(2026, 10, 5), card.NextPaymentDueDate); // efetivo = manual, já que o Plaid não informou
        Assert.Equal(1000m, card.CreditLimit); // efetivo = manual
        Assert.Equal(10m, card.TargetPercent);
    }

    [Fact]
    public async Task UpdateCard_returns_not_found_for_an_account_belonging_to_another_user()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = "someone-else",
                ItemId = "item-2",
                AccountId = "acc-not-mine",
                Name = "Not mine",
                Type = "Credit",
                CurrentBalance = 100m,
                CreditLimit = 1000m,
            });
        });

        var request = new UpdateCardRequest(null, null, null, null);
        var response = await _client.PutAsJsonAsync("/api/cards/acc-not-mine", request, JsonDefaults.Options);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadReport_returns_a_csv_file_with_a_header_row()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId,
                ItemId = "item-1",
                AccountId = "acc-1",
                Name = "Test Visa",
                Type = "Credit",
                CurrentBalance = 450m,
                CreditLimit = 1000m,
            });
        });

        var response = await _client.GetAsync("/api/cards/report");
        response.EnsureSuccessStatusCode();

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var csv = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("Situacao,Cartao,Instituicao", csv);
        Assert.Contains("Test Visa", csv);
    }

    [Fact]
    public async Task GetHistory_returns_not_found_for_an_account_belonging_to_another_user()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = "someone-else",
                ItemId = "item-2",
                AccountId = "acc-not-mine",
                Name = "Not mine",
                Type = "Credit",
            });
        });

        var response = await _client.GetAsync("/api/cards/acc-not-mine/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetHistory_returns_snapshots_ordered_by_date()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId,
                ItemId = "item-1",
                AccountId = "acc-1",
                Name = "Test Visa",
                Type = "Credit",
            });
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            db.CardBalanceSnapshots.Add(new CardBalanceSnapshot
            {
                UserId = TestAuthHandler.TestUserId, AccountId = "acc-1", Date = today.AddDays(-1),
                Balance = 100m, Limit = 1000m, UtilizationPercent = 10m,
            });
            db.CardBalanceSnapshots.Add(new CardBalanceSnapshot
            {
                UserId = TestAuthHandler.TestUserId, AccountId = "acc-1", Date = today.AddDays(-3),
                Balance = 80m, Limit = 1000m, UtilizationPercent = 8m,
            });
        });

        var points = await _client.GetFromJsonAsync<List<CardHistoryPointDto>>("/api/cards/acc-1/history", JsonDefaults.Options);

        Assert.Equal(2, points!.Count);
        Assert.True(points[0].Date < points[1].Date);
    }

    [Fact]
    public async Task PayoffPlan_prioritizes_the_card_closing_soonest()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Escolhe os dois dias de fechamento de forma que ambos "caiam do mesmo lado" da
        // lógica de rollover de CardMath (os dois ainda não passaram esse mês, ou os dois já
        // passaram e viram mês que vem) — assim a ordem soonDay < laterDay sempre vira
        // soonDate < laterDate, não importa em que dia do mês o teste rodar.
        int soonDay, laterDay;
        if (today.Day <= 20)
        {
            soonDay = today.Day + 2;
            laterDay = today.Day + 9;
        }
        else
        {
            laterDay = Math.Min(today.Day, 28);
            soonDay = Math.Max(1, laterDay - 5);
        }

        await _factory.SeedAsync(db =>
        {
            db.UserSettings.Add(new UserSettings
            {
                UserId = TestAuthHandler.TestUserId, GlobalTargetUtilizationPercent = 30m, NotifyDaysBeforeClosing = 3,
            });
            // Fecha depois, mas precisa de mais dinheiro.
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "later",
                Name = "Closes later", Type = "Credit", CurrentBalance = 900m, CreditLimit = 1000m,
                StatementClosingDay = laterDay,
            });
            // Fecha antes, precisa de menos dinheiro — deve vir primeiro na lista.
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "sooner",
                Name = "Closes sooner", Type = "Credit", CurrentBalance = 400m, CreditLimit = 1000m,
                StatementClosingDay = soonDay,
            });
        });

        var response = await _client.PostAsJsonAsync("/api/cards/payoff-plan", new { availableAmount = 1000m }, JsonDefaults.Options);
        response.EnsureSuccessStatusCode();
        var plan = await response.Content.ReadFromJsonAsync<PayoffPlanResponseDto>(JsonDefaults.Options);

        Assert.Equal("sooner", plan!.Allocations[0].AccountId);
        Assert.Equal("later", plan.Allocations[1].AccountId);
    }

    [Fact]
    public async Task PayoffPlan_caps_each_allocation_to_what_the_card_needs_and_reports_the_leftover()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.UserSettings.Add(new UserSettings
            {
                UserId = TestAuthHandler.TestUserId, GlobalTargetUtilizationPercent = 30m, NotifyDaysBeforeClosing = 3,
            });
            // Precisa de 100 (400 - 30% de 1000) pra bater a meta.
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "card-1",
                Name = "Only card", Type = "Credit", CurrentBalance = 400m, CreditLimit = 1000m,
                StatementClosingDay = today.AddDays(10).Day,
            });
        });

        var response = await _client.PostAsJsonAsync("/api/cards/payoff-plan", new { availableAmount = 1000m }, JsonDefaults.Options);
        var plan = await response.Content.ReadFromJsonAsync<PayoffPlanResponseDto>(JsonDefaults.Options);

        var allocation = Assert.Single(plan!.Allocations);
        Assert.Equal(100m, allocation.AmountToPay);
        Assert.Equal(900m, plan.RemainingUnallocated);
    }

    [Fact]
    public async Task PayoffPlan_skips_cards_already_within_target()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.UserSettings.Add(new UserSettings
            {
                UserId = TestAuthHandler.TestUserId, GlobalTargetUtilizationPercent = 30m, NotifyDaysBeforeClosing = 3,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "card-1",
                Name = "Already fine", Type = "Credit", CurrentBalance = 200m, CreditLimit = 1000m,
                StatementClosingDay = today.AddDays(10).Day,
            });
        });

        var response = await _client.PostAsJsonAsync("/api/cards/payoff-plan", new { availableAmount = 500m }, JsonDefaults.Options);
        var plan = await response.Content.ReadFromJsonAsync<PayoffPlanResponseDto>(JsonDefaults.Options);

        Assert.Empty(plan!.Allocations);
        Assert.Equal(500m, plan.RemainingUnallocated);
    }
}
