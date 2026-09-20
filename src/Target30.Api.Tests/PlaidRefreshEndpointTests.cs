using System.Net;
using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

// Nenhum destes testes chega a falar com o Plaid (refresh é cobrado): cobrem só o que responde
// antes da chamada externa — item inexistente/de outro usuário e a trava de intervalo.
public class PlaidRefreshEndpointTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PlaidRefreshEndpointTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static PlaidItem Item(string userId, string itemId, DateTime? lastRefresh = null) => new()
    {
        UserId = userId, ItemId = itemId, AccessToken = "access-fake", InstitutionName = "Bank",
        LastRefreshRequestedAt = lastRefresh,
    };

    [Fact]
    public async Task Refresh_returns_404_for_an_unknown_item()
    {
        var response = await _client.PostAsync("/api/plaid/items/nope/refresh", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_returns_404_for_an_item_that_belongs_to_someone_else()
    {
        await _factory.SeedAsync(db => db.PlaidItems.Add(Item("someone-else", "other-item")));

        var response = await _client.PostAsync("/api/plaid/items/other-item/refresh", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_is_blocked_with_429_and_retry_after_inside_the_cooldown()
    {
        await _factory.SeedAsync(db =>
            db.PlaidItems.Add(Item(TestAuthHandler.TestUserId, "item-1", DateTime.UtcNow.AddMinutes(-2))));

        var response = await _client.PostAsync("/api/plaid/items/item-1/refresh", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var retryAfter = int.Parse(response.Headers.GetValues("Retry-After").Single());
        Assert.InRange(retryAfter, 1, (int)PlaidRefreshGuard.Cooldown.TotalSeconds);
    }

    [Fact]
    public async Task Freshness_is_empty_when_the_user_has_no_items()
    {
        var result = await _client.GetFromJsonAsync<List<PlaidItemFreshnessDto>>(
            "/api/plaid/items/freshness", JsonDefaults.Options);

        Assert.Empty(result!);
    }
}
