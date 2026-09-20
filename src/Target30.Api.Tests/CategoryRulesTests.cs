using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class MerchantKeyTests
{
    [Theory]
    [InlineData(null, "CHECKCARD 0916 Cherry Technol", "checkcard cherry technol")]
    [InlineData(null, "CHECKCARD 0917 Cherry Technol", "checkcard cherry technol")]
    [InlineData("Costco", "COSTCO WHSE #1234", "costco")]
    [InlineData("  Uber   Eats ", "x", "uber eats")]
    [InlineData(null, "12345", "")]
    public void Builds_a_stable_key_for_the_same_merchant(string? merchant, string name, string expected)
    {
        Assert.Equal(expected, MerchantKey.From(merchant, name));
    }

    [Fact]
    public void RuleFor_finds_the_rule_by_key_and_ignores_unknown_merchants()
    {
        var rules = new Dictionary<string, string> { ["costco"] = "GROCERIES" };

        Assert.Equal("GROCERIES", MerchantKey.RuleFor(rules, "Costco", "x"));
        Assert.Null(MerchantKey.RuleFor(rules, "Target", "x"));
        Assert.Null(MerchantKey.RuleFor(new Dictionary<string, string>(), "Costco", "x"));
    }
}

public class CategoryRuleEndpointTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CategoryRuleEndpointTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static PlaidTransaction Tx(string id, string merchant, string? user = null) => new()
    {
        UserId = user ?? TestAuthHandler.TestUserId, PlaidTransactionId = id, AccountId = "a", ItemId = "i",
        Amount = 10m, Date = new DateOnly(2026, 9, 1), Name = merchant, MerchantName = merchant, Category = "GENERAL_MERCHANDISE",
    };

    private Task SeedThreeCostcoAndOneOther() => _factory.SeedAsync(db =>
    {
        db.PlaidTransactions.Add(Tx("c1", "Costco"));
        db.PlaidTransactions.Add(Tx("c2", "Costco"));
        db.PlaidTransactions.Add(Tx("c3", "COSTCO #99"));
        db.PlaidTransactions.Add(Tx("t1", "Target"));
        db.PlaidTransactions.Add(Tx("other-user", "Costco", user: "someone-else"));
    });

    private async Task<HttpResponseMessage> Categorize(string id, string? category, bool applyToMerchant) =>
        await _client.PutAsJsonAsync($"/api/plaid/transactions/{id}/category",
            new UpdateCategoryRequest(category, applyToMerchant), JsonDefaults.Options);

    private async Task<Dictionary<string, string?>> CategoriesByIdAsync()
    {
        var page = await _client.GetFromJsonAsync<PageDto>("/api/plaid/transactions?pageSize=100", JsonDefaults.Options);
        return page!.Items.ToDictionary(t => t.TransactionId, t => t.IsCategoryCustom ? t.Category : null);
    }

    private record PageDto(List<TransactionDto> Items);

    [Fact]
    public async Task Without_apply_only_that_transaction_changes_and_no_rule_is_created()
    {
        await SeedThreeCostcoAndOneOther();

        await Categorize("c1", "FOOD_AND_DRINK", applyToMerchant: false);

        var cats = await CategoriesByIdAsync();
        Assert.Equal("FOOD_AND_DRINK", cats["c1"]);
        Assert.Null(cats["c2"]);
        Assert.Empty((await _client.GetFromJsonAsync<List<CategoryRuleDto>>("/api/category-rules", JsonDefaults.Options))!);
    }

    [Fact]
    public async Task Applying_to_the_merchant_recategorizes_the_matching_ones_and_creates_a_rule()
    {
        await SeedThreeCostcoAndOneOther();

        var response = await Categorize("c1", "FOOD_AND_DRINK", applyToMerchant: true);

        Assert.Equal("3", response.Headers.GetValues("X-Applied-Count").Single());
        var cats = await CategoriesByIdAsync();
        Assert.Equal("FOOD_AND_DRINK", cats["c1"]);
        Assert.Equal("FOOD_AND_DRINK", cats["c2"]);
        Assert.Equal("FOOD_AND_DRINK", cats["c3"]);
        Assert.Null(cats["t1"]);

        var rule = Assert.Single((await _client.GetFromJsonAsync<List<CategoryRuleDto>>("/api/category-rules", JsonDefaults.Options))!);
        Assert.Equal("costco", rule.MerchantKey);
        Assert.Equal("FOOD_AND_DRINK", rule.Category);
    }

    [Fact]
    public async Task Never_touches_another_users_transactions()
    {
        await SeedThreeCostcoAndOneOther();

        await Categorize("c1", "FOOD_AND_DRINK", applyToMerchant: true);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.Target30DbContext>();
        Assert.Null(db.PlaidTransactions.Single(t => t.PlaidTransactionId == "other-user").UserCategory);
    }

    [Fact]
    public async Task Changing_the_category_again_updates_the_same_rule_and_clearing_removes_it()
    {
        await SeedThreeCostcoAndOneOther();
        await Categorize("c1", "FOOD_AND_DRINK", applyToMerchant: true);

        await Categorize("c1", "GENERAL_SERVICES", applyToMerchant: true);
        var rule = Assert.Single((await _client.GetFromJsonAsync<List<CategoryRuleDto>>("/api/category-rules", JsonDefaults.Options))!);
        Assert.Equal("GENERAL_SERVICES", rule.Category);

        await Categorize("c1", null, applyToMerchant: true);
        Assert.Empty((await _client.GetFromJsonAsync<List<CategoryRuleDto>>("/api/category-rules", JsonDefaults.Options))!);
        Assert.Null((await CategoriesByIdAsync())["c2"]);
    }

    [Fact]
    public async Task Deleting_a_rule_keeps_what_was_already_recategorized()
    {
        await SeedThreeCostcoAndOneOther();
        await Categorize("c1", "FOOD_AND_DRINK", applyToMerchant: true);
        var rule = (await _client.GetFromJsonAsync<List<CategoryRuleDto>>("/api/category-rules", JsonDefaults.Options))!.Single();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/category-rules/{rule.Id}")).StatusCode);

        Assert.Empty((await _client.GetFromJsonAsync<List<CategoryRuleDto>>("/api/category-rules", JsonDefaults.Options))!);
        Assert.Equal("FOOD_AND_DRINK", (await CategoriesByIdAsync())["c2"]);
    }

    [Fact]
    public async Task Cannot_delete_someone_elses_rule()
    {
        await _factory.SeedAsync(db => db.CategoryRules.Add(new CategoryRule
        {
            Id = 555, UserId = "someone-else", MerchantKey = "x", Category = "Y",
        }));

        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/category-rules/555")).StatusCode);
    }
}
