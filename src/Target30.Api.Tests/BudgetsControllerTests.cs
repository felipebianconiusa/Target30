using System.Net;
using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class BudgetsControllerTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BudgetsControllerTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetBudgets_computes_current_month_spend_and_percent_used()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);

        await _factory.SeedAsync(db =>
        {
            db.CategoryBudgets.Add(new CategoryBudget
            {
                UserId = TestAuthHandler.TestUserId, Category = "FOOD_AND_DRINK", MonthlyLimit = 200m,
            });
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t1", AccountId = "a",
                ItemId = "i", Amount = 150m, Date = firstOfMonth, Name = "Restaurant",
                Category = "FOOD_AND_DRINK",
            });
            // Fora da categoria do orçamento — não deve contar.
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t2", AccountId = "a",
                ItemId = "i", Amount = 999m, Date = firstOfMonth, Name = "Rent",
                Category = "RENT_AND_UTILITIES",
            });
        });

        var budgets = await _client.GetFromJsonAsync<List<BudgetDto>>("/api/budgets", JsonDefaults.Options);

        var budget = Assert.Single(budgets!);
        Assert.Equal(150m, budget.CurrentSpend);
        Assert.Equal(75m, budget.PercentUsed);
    }

    [Fact]
    public async Task GetBudgets_ignores_transactions_from_previous_months()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lastMonth = new DateOnly(today.Year, today.Month, 1).AddDays(-1);

        await _factory.SeedAsync(db =>
        {
            db.CategoryBudgets.Add(new CategoryBudget
            {
                UserId = TestAuthHandler.TestUserId, Category = "FOOD_AND_DRINK", MonthlyLimit = 200m,
            });
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t1", AccountId = "a",
                ItemId = "i", Amount = 150m, Date = lastMonth, Name = "Restaurant",
                Category = "FOOD_AND_DRINK",
            });
        });

        var budgets = await _client.GetFromJsonAsync<List<BudgetDto>>("/api/budgets", JsonDefaults.Options);

        var budget = Assert.Single(budgets!);
        Assert.Equal(0m, budget.CurrentSpend);
    }

    [Fact]
    public async Task GetBudgets_uses_the_user_category_override_when_present()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);

        await _factory.SeedAsync(db =>
        {
            db.CategoryBudgets.Add(new CategoryBudget
            {
                UserId = TestAuthHandler.TestUserId, Category = "ENTERTAINMENT", MonthlyLimit = 50m,
            });
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t1", AccountId = "a",
                ItemId = "i", Amount = 60m, Date = firstOfMonth, Name = "Mystery Charge",
                Category = "GENERAL_MERCHANDISE", UserCategory = "ENTERTAINMENT",
            });
        });

        var budgets = await _client.GetFromJsonAsync<List<BudgetDto>>("/api/budgets", JsonDefaults.Options);

        var budget = Assert.Single(budgets!);
        Assert.Equal(60m, budget.CurrentSpend);
    }

    [Fact]
    public async Task CreateBudget_rejects_a_duplicate_category()
    {
        await _factory.SeedAsync(db =>
        {
            db.CategoryBudgets.Add(new CategoryBudget
            {
                UserId = TestAuthHandler.TestUserId, Category = "FOOD_AND_DRINK", MonthlyLimit = 200m,
            });
        });

        var response = await _client.PostAsJsonAsync(
            "/api/budgets", new BudgetRequest("FOOD_AND_DRINK", 300m), JsonDefaults.Options);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetBudgets_only_returns_budgets_belonging_to_the_current_user()
    {
        await _factory.SeedAsync(db =>
        {
            db.CategoryBudgets.Add(new CategoryBudget
            {
                UserId = "someone-else", Category = "FOOD_AND_DRINK", MonthlyLimit = 200m,
            });
        });

        var budgets = await _client.GetFromJsonAsync<List<BudgetDto>>("/api/budgets", JsonDefaults.Options);
        Assert.Empty(budgets!);
    }
}
