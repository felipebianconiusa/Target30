using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class TransactionClassifierTests
{
    [Theory]
    [InlineData("LOAN_PAYMENTS_CREDIT_CARD_PAYMENT", true)]
    [InlineData("TRANSFER_OUT_ACCOUNT_TRANSFER", true)]
    [InlineData("TRANSFER_IN_ACCOUNT_TRANSFER", true)]
    // Pagamentos de dívida de verdade e transferências por app são gasto/receita reais.
    [InlineData("LOAN_PAYMENTS_CAR_PAYMENT", false)]
    [InlineData("LOAN_PAYMENTS_BNPL", false)]
    [InlineData("TRANSFER_IN_TRANSFER_IN_FROM_APPS", false)]
    [InlineData("TRANSFER_OUT_TRANSFER_OUT_FROM_APPS", false)]
    [InlineData("FOOD_AND_DRINK_RESTAURANT", false)]
    [InlineData(null, false)]
    public void IsInternalTransfer_only_flags_money_moving_between_the_users_own_accounts(string? detailed, bool expected)
    {
        Assert.Equal(expected, TransactionClassifier.IsInternalTransfer(detailed));
    }
}

public class InternalTransferTotalsTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public InternalTransferTotalsTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static PlaidTransaction Tx(string id, decimal amount, DateOnly date, string category, bool internalTransfer = false) => new()
    {
        UserId = TestAuthHandler.TestUserId, PlaidTransactionId = id, AccountId = "a", ItemId = "i",
        Amount = amount, Date = date, Name = id, Category = category, IsInternalTransfer = internalTransfer,
    };

    // Uma compra de 50, um salário de 200 e um pagamento de fatura de 100 que aparece nas duas
    // pontas (saída na corrente, "entrada" no cartão).
    private Task SeedAsync(DateOnly date) => _factory.SeedAsync(db =>
    {
        db.PlaidTransactions.Add(Tx("purchase", 50m, date, "FOOD_AND_DRINK"));
        db.PlaidTransactions.Add(Tx("salary", -200m, date, "INCOME"));
        db.PlaidTransactions.Add(Tx("card-payment-out", 100m, date, "LOAN_PAYMENTS", internalTransfer: true));
        db.PlaidTransactions.Add(Tx("card-payment-in", -100m, date, "LOAN_PAYMENTS", internalTransfer: true));
    });

    [Fact]
    public async Task Totals_do_not_count_card_payments_but_the_list_still_shows_them()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedAsync(today);

        var totals = await _client.GetFromJsonAsync<TotalsDto>("/api/plaid/transactions/totals", JsonDefaults.Options);
        var page = await _client.GetFromJsonAsync<PageDto>("/api/plaid/transactions", JsonDefaults.Options);

        Assert.Equal(50m, totals!.TotalExpenses);
        Assert.Equal(200m, totals.TotalIncome);
        Assert.Equal(4, page!.Total);
        Assert.Equal(2, page.Items.Count(t => t.IsInternalTransfer));
    }

    [Fact]
    public async Task Summary_ignores_card_payments_in_totals_and_categories_but_keeps_them_in_recent()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await SeedAsync(today);

        var summary = await _client.GetFromJsonAsync<SummaryDto>("/api/plaid/summary", JsonDefaults.Options);

        Assert.Equal(50m, summary!.TotalExpenses);
        Assert.Equal(200m, summary.TotalIncome);
        Assert.DoesNotContain(summary.CategoryTotals, c => c.Category == "LOAN_PAYMENTS");
        Assert.Equal(4, summary.RecentTransactions.Count);
    }

    [Fact]
    public async Task Budgets_do_not_count_a_card_payment_as_spending_in_its_category()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.CategoryBudgets.Add(new CategoryBudget
            {
                UserId = TestAuthHandler.TestUserId, Category = "LOAN_PAYMENTS", MonthlyLimit = 1000m,
            });
            db.PlaidTransactions.Add(Tx("card-payment", 400m, new DateOnly(today.Year, today.Month, 1), "LOAN_PAYMENTS", internalTransfer: true));
            db.PlaidTransactions.Add(Tx("car", 300m, new DateOnly(today.Year, today.Month, 1), "LOAN_PAYMENTS"));
        });

        var budgets = await _client.GetFromJsonAsync<List<BudgetDto>>("/api/budgets", JsonDefaults.Options);

        Assert.Equal(300m, Assert.Single(budgets!).CurrentSpend);
    }

    private record TotalsDto(decimal TotalIncome, decimal TotalExpenses);
    private record PageDto(List<TransactionDto> Items, int Total);
    private record CategoryTotalDto(string Category, decimal Total);
    private record SummaryDto(
        decimal TotalIncome, decimal TotalExpenses, List<CategoryTotalDto> CategoryTotals,
        List<TransactionDto> RecentTransactions);
}
