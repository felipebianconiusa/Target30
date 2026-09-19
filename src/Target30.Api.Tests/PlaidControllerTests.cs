using System.Net;
using System.Net.Http.Json;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class PlaidControllerTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PlaidControllerTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UpdateTransactionCategory_overrides_the_category_and_persists_across_reads()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t1", AccountId = "acc-1",
                ItemId = "item-1", Amount = 10m, Date = new DateOnly(2026, 1, 1), Name = "Mystery Charge",
                Category = "GENERAL_MERCHANDISE",
            });
        });

        var putResponse = await _client.PutAsJsonAsync(
            "/api/plaid/transactions/t1/category", new { category = "FOOD_AND_DRINK" }, JsonDefaults.Options);
        putResponse.EnsureSuccessStatusCode();

        var updated = await putResponse.Content.ReadFromJsonAsync<TransactionDtoForTest>(JsonDefaults.Options);
        Assert.Equal("FOOD_AND_DRINK", updated!.Category);
        Assert.True(updated.IsCategoryCustom);

        var page = await _client.GetFromJsonAsync<PagedTransactionsForTest>("/api/plaid/transactions", JsonDefaults.Options);
        var tx = Assert.Single(page!.Items);
        Assert.Equal("FOOD_AND_DRINK", tx.Category);
    }

    [Fact]
    public async Task UpdateTransactionCategory_with_null_reverts_to_the_plaid_category()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t1", AccountId = "acc-1",
                ItemId = "item-1", Amount = 10m, Date = new DateOnly(2026, 1, 1), Name = "Mystery Charge",
                Category = "GENERAL_MERCHANDISE", UserCategory = "FOOD_AND_DRINK",
            });
        });

        var response = await _client.PutAsJsonAsync(
            "/api/plaid/transactions/t1/category", new { category = (string?)null }, JsonDefaults.Options);
        var updated = await response.Content.ReadFromJsonAsync<TransactionDtoForTest>(JsonDefaults.Options);

        Assert.Equal("GENERAL_MERCHANDISE", updated!.Category);
        Assert.False(updated.IsCategoryCustom);
    }

    [Fact]
    public async Task UpdateTransactionCategory_returns_not_found_for_a_transaction_belonging_to_another_user()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = "someone-else", PlaidTransactionId = "t1", AccountId = "acc-1",
                ItemId = "item-1", Amount = 10m, Date = new DateOnly(2026, 1, 1), Name = "Not mine",
            });
        });

        var response = await _client.PutAsJsonAsync(
            "/api/plaid/transactions/t1/category", new { category = "FOOD_AND_DRINK" }, JsonDefaults.Options);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record TransactionDtoForTest(string TransactionId, string? Category, bool IsCategoryCustom);
    private record PagedTransactionsForTest(List<TransactionDtoForTest> Items);
}
