using Target30.Api.Models;

namespace Target30.Api.Tests;

public class BackupControllerTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BackupControllerTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Export_never_includes_the_plaid_access_token()
    {
        const string secretAccessToken = "access-sandbox-super-secret-do-not-leak";
        await _factory.SeedAsync(db =>
        {
            db.PlaidItems.Add(new PlaidItem
            {
                UserId = TestAuthHandler.TestUserId,
                ItemId = "item-1",
                AccessToken = secretAccessToken,
                InstitutionName = "Test Bank",
            });
        });

        var response = await _client.GetAsync("/api/backup/export");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(secretAccessToken, json);
        Assert.DoesNotContain("AccessToken", json);
        Assert.Contains("Test Bank", json);
    }

    [Fact]
    public async Task Export_only_includes_data_belonging_to_the_current_user()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "mine",
                Name = "My Card", Type = "Credit",
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = "someone-else", ItemId = "item-2", AccountId = "not-mine",
                Name = "Someone Else's Card", Type = "Credit",
            });
        });

        var response = await _client.GetAsync("/api/backup/export");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Contains("My Card", json);
        Assert.DoesNotContain("Someone Else's Card", json);
    }

    [Fact]
    public async Task Export_returns_a_downloadable_json_file()
    {
        var response = await _client.GetAsync("/api/backup/export");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
    }
}
