using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class DashboardControllerTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DashboardControllerTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MonthlyComparison_compares_the_same_number_of_days_in_each_month()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var firstOfLastMonth = firstOfThisMonth.AddMonths(-1);
        // Cai no último dia comparável do mês passado (mesmo "dia do mês" que hoje).
        var comparableLastMonthDate = new DateOnly(firstOfLastMonth.Year, firstOfLastMonth.Month,
            Math.Min(today.Day, DateTime.DaysInMonth(firstOfLastMonth.Year, firstOfLastMonth.Month)));
        // Depois do ponto de corte — não deveria entrar na comparação.
        var afterCutoffLastMonth = comparableLastMonthDate.AddDays(1);

        await _factory.SeedAsync(db =>
        {
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "cur", AccountId = "a", ItemId = "i",
                Amount = 100m, Date = firstOfThisMonth, Name = "Groceries", Category = "FOOD_AND_DRINK",
            });
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "prev", AccountId = "a", ItemId = "i",
                Amount = 50m, Date = comparableLastMonthDate, Name = "Groceries", Category = "FOOD_AND_DRINK",
            });
            if (afterCutoffLastMonth.Month == firstOfLastMonth.Month)
            {
                db.PlaidTransactions.Add(new PlaidTransaction
                {
                    UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "prev-after", AccountId = "a", ItemId = "i",
                    Amount = 999m, Date = afterCutoffLastMonth, Name = "Should not count", Category = "FOOD_AND_DRINK",
                });
            }
        });

        var rows = await _client.GetFromJsonAsync<List<MonthlyComparisonRowDto>>(
            "/api/dashboard/monthly-comparison", JsonDefaults.Options);

        var row = Assert.Single(rows!);
        Assert.Equal("FOOD_AND_DRINK", row.Category);
        Assert.Equal(100m, row.CurrentMonthTotal);
        Assert.Equal(50m, row.PreviousMonthTotal);
        Assert.Equal(100m, row.ChangePercent);
    }

    [Fact]
    public async Task HealthScore_reflects_card_utilization_and_spending_trend()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);

        await _factory.SeedAsync(db =>
        {
            db.UserSettings.Add(new UserSettings
            {
                UserId = TestAuthHandler.TestUserId, GlobalTargetUtilizationPercent = 30m, NotifyDaysBeforeClosing = 3,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "card-1", Name = "Card",
                Type = "Credit", CurrentBalance = 300m, CreditLimit = 1000m, // 30% = na meta
            });
        });

        var score = await _client.GetFromJsonAsync<HealthScoreDtoForTest>(
            "/api/dashboard/health-score", JsonDefaults.Options);

        Assert.Equal(100, score!.CardComponent);
        Assert.Equal(100, score.SpendingComponent);
        Assert.Equal(100, score.Score);
    }

    private record HealthScoreDtoForTest(int Score, int CardComponent, int SpendingComponent, string Label);
}
