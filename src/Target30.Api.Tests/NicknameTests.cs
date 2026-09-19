using System.Net;
using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class NicknameTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public NicknameTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task SeedCard() => _factory.SeedAsync(db =>
        db.PlaidAccounts.Add(new PlaidAccount
        {
            UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "acc", Name = "Savor",
            Type = "Credit", CurrentBalance = 100m, CreditLimit = 1000m,
        }));

    [Theory]
    [InlineData(null, "Savor")]
    [InlineData("", "Savor")]
    [InlineData("   ", "Savor")]
    [InlineData("Cartão do mercado", "Cartão do mercado (Savor)")]
    public void DisplayName_shows_the_nickname_with_the_original_name(string? nickname, string expected)
    {
        var account = new PlaidAccount { Name = "Savor", Nickname = nickname };
        Assert.Equal(expected, account.DisplayName);
    }

    [Fact]
    public async Task UpdateCard_saves_the_nickname_and_the_original_name_is_still_returned()
    {
        await SeedCard();

        var put = await _client.PutAsJsonAsync("/api/cards/acc",
            new UpdateCardRequest(null, null, null, null, "  Mercado  "), JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var cards = await _client.GetFromJsonAsync<List<CardDto>>("/api/cards", JsonDefaults.Options);
        var card = Assert.Single(cards!);
        Assert.Equal("Mercado", card.Nickname);
        Assert.Equal("Savor", card.Name);
    }

    [Fact]
    public async Task UpdateCard_with_an_empty_nickname_clears_it()
    {
        await _factory.SeedAsync(db =>
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "acc", Name = "Savor",
                Type = "Credit", Nickname = "Old",
            }));

        await _client.PutAsJsonAsync("/api/cards/acc",
            new UpdateCardRequest(null, null, null, null, ""), JsonDefaults.Options);

        var cards = await _client.GetFromJsonAsync<List<CardDto>>("/api/cards", JsonDefaults.Options);
        Assert.Null(Assert.Single(cards!).Nickname);
    }

    [Fact]
    public async Task UpdateCard_truncates_a_very_long_nickname()
    {
        await SeedCard();

        await _client.PutAsJsonAsync("/api/cards/acc",
            new UpdateCardRequest(null, null, null, null, new string('x', 200)), JsonDefaults.Options);

        var cards = await _client.GetFromJsonAsync<List<CardDto>>("/api/cards", JsonDefaults.Options);
        Assert.Equal(60, Assert.Single(cards!).Nickname!.Length);
    }

    [Fact]
    public async Task CashFlow_entries_use_the_nickname_together_with_the_original_name()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "acc", Name = "Savor",
                Type = "Credit", CurrentBalance = 50m, Nickname = "Mercado",
                ManualNextPaymentDueDate = today.AddDays(10),
            }));

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>(
            "/api/cashflow?pastDays=0&futureDays=30", JsonDefaults.Options);

        Assert.Contains(response!.Entries, e => e.Description == "Mercado (Savor) - Fatura");
    }
}
