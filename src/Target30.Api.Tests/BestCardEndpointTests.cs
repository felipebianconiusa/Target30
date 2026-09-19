using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class BestCardEndpointTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BestCardEndpointTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task BestToday_recommends_a_card_with_room_and_lists_the_maxed_one_as_excluded()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "maxed", Name = "Maxed",
                Type = "Credit", CurrentBalance = 1000m, CreditLimit = 1000m, StatementClosingDay = 28,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "free", Name = "Free",
                Type = "Credit", CurrentBalance = 50m, CreditLimit = 1000m, StatementClosingDay = 10,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = "someone-else", ItemId = "i2", AccountId = "not-mine", Name = "Not mine",
                Type = "Credit", CurrentBalance = 0m, CreditLimit = 5000m, StatementClosingDay = 15,
            });
        });

        var result = await _client.GetFromJsonAsync<BestCardResponseDto>("/api/cards/best-today", JsonDefaults.Options);

        Assert.Equal("Free", result!.Recommended!.Name);
        Assert.Equal("Maxed", Assert.Single(result.Excluded).Name);
        Assert.Equal("limit_reached", result.Excluded[0].ExclusionReason);
    }

    [Fact]
    public async Task BestToday_has_no_recommendation_when_there_are_no_cards()
    {
        var result = await _client.GetFromJsonAsync<BestCardResponseDto>("/api/cards/best-today", JsonDefaults.Options);

        Assert.Null(result!.Recommended);
        Assert.Empty(result.Ranking);
    }
}
