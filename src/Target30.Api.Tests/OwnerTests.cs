using System.Net;
using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class OwnerTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OwnerTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static PlaidAccount Card(string id, string? owner = null) => new()
    {
        UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = id, Name = id, Type = "Credit",
        CurrentBalance = 100m, CreditLimit = 1000m, Owner = owner,
    };

    private static PlaidTransaction Tx(string id, string accountId, decimal amount) => new()
    {
        UserId = TestAuthHandler.TestUserId, PlaidTransactionId = id, AccountId = accountId, ItemId = "i",
        Amount = amount, Date = new DateOnly(2026, 9, 1), Name = id,
    };

    [Fact]
    public async Task UpdateCard_saves_a_trimmed_owner_and_an_empty_one_clears_it()
    {
        await _factory.SeedAsync(db => db.PlaidAccounts.Add(Card("acc")));

        await _client.PutAsJsonAsync("/api/cards/acc",
            new UpdateCardRequest(null, null, null, null, null, "  Layse  "), JsonDefaults.Options);
        var cards = await _client.GetFromJsonAsync<List<CardDto>>("/api/cards", JsonDefaults.Options);
        Assert.Equal("Layse", Assert.Single(cards!).Owner);

        await _client.PutAsJsonAsync("/api/cards/acc",
            new UpdateCardRequest(null, null, null, null, null, "   "), JsonDefaults.Options);
        cards = await _client.GetFromJsonAsync<List<CardDto>>("/api/cards", JsonDefaults.Options);
        Assert.Null(Assert.Single(cards!).Owner);
    }

    [Fact]
    public async Task UpdateCard_truncates_a_very_long_owner()
    {
        await _factory.SeedAsync(db => db.PlaidAccounts.Add(Card("acc")));

        await _client.PutAsJsonAsync("/api/cards/acc",
            new UpdateCardRequest(null, null, null, null, null, new string('x', 100)), JsonDefaults.Options);

        var cards = await _client.GetFromJsonAsync<List<CardDto>>("/api/cards", JsonDefaults.Options);
        Assert.Equal(40, Assert.Single(cards!).Owner!.Length);
    }

    [Fact]
    public async Task Owners_lists_the_distinct_owners_in_use()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(Card("a", "Layse"));
            db.PlaidAccounts.Add(Card("b", "Felipe"));
            db.PlaidAccounts.Add(Card("c", "Layse"));
            db.PlaidAccounts.Add(Card("d"));
        });

        var owners = await _client.GetFromJsonAsync<List<string>>("/api/plaid/owners", JsonDefaults.Options);

        Assert.Equal(["Felipe", "Layse"], owners);
    }

    [Fact]
    public async Task Transactions_and_totals_can_be_filtered_by_owner()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(Card("felipe-card", "Felipe"));
            db.PlaidAccounts.Add(Card("layse-card", "Layse"));
            db.PlaidTransactions.Add(Tx("f1", "felipe-card", 30m));
            db.PlaidTransactions.Add(Tx("l1", "layse-card", 70m));
            db.PlaidTransactions.Add(Tx("l2", "layse-card", 5m));
        });

        var page = await _client.GetFromJsonAsync<PageDto>("/api/plaid/transactions?owners=Layse", JsonDefaults.Options);
        var totals = await _client.GetFromJsonAsync<TotalsDto>("/api/plaid/transactions/totals?owners=Layse", JsonDefaults.Options);

        Assert.Equal(2, page!.Total);
        Assert.All(page.Items, t => Assert.Equal("layse-card", t.AccountId));
        Assert.Equal(75m, totals!.TotalExpenses);
    }

    private record PageDto(List<TransactionDto> Items, int Total);
    private record TotalsDto(decimal TotalIncome, decimal TotalExpenses);
}
