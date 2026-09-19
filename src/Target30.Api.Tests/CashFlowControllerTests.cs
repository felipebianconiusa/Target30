using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class CashFlowControllerTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CashFlowControllerTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetCashFlow_inverts_the_plaid_sign_so_positive_amount_means_money_in()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "checking",
                Name = "Checking", Type = "Depository", CurrentBalance = 1000m,
            });
            // No Plaid, Amount positivo = saída. Uma compra de 50 deve virar -50 na resposta.
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t1", AccountId = "checking",
                ItemId = "item-1", Amount = 50m, Date = today, Name = "Grocery Store", Pending = false,
            });
        });

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>("/api/cashflow", JsonDefaults.Options);

        var entry = Assert.Single(response!.Entries);
        Assert.Equal(-50m, entry.Amount);
        Assert.Equal("Done", entry.Status);
    }

    [Fact]
    public async Task GetCashFlow_running_balance_ends_at_the_current_depository_balance()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "checking",
                Name = "Checking", Type = "Depository", CurrentBalance = 1000m,
            });
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t1", AccountId = "checking",
                ItemId = "item-1", Amount = 100m, Date = today.AddDays(-2), Name = "Purchase", Pending = false,
            });
        });

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>("/api/cashflow", JsonDefaults.Options);

        Assert.Equal(1000m, response!.CurrentBalance);
        Assert.Equal(1000m, response.Entries[^1].Balance);
    }

    [Fact]
    public async Task GetCashFlow_projects_future_occurrences_of_active_recurring_bills()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.RecurringBills.Add(new RecurringBill
            {
                UserId = TestAuthHandler.TestUserId, Description = "Rent", Amount = 500m,
                DayOfMonth = today.AddDays(10).Day, IsActive = true,
            });
            db.RecurringBills.Add(new RecurringBill
            {
                UserId = TestAuthHandler.TestUserId, Description = "Cancelled subscription", Amount = 20m,
                DayOfMonth = today.AddDays(5).Day, IsActive = false,
            });
        });

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>(
            "/api/cashflow?pastDays=0&futureDays=45", JsonDefaults.Options);

        Assert.Contains(response!.Entries, e => e.Description == "Rent" && e.Amount == -500m);
        Assert.DoesNotContain(response.Entries, e => e.Description == "Cancelled subscription");
    }

    [Fact]
    public async Task GetCashFlow_adds_a_card_due_date_entry_using_the_manual_override_when_plaid_has_no_due_date()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "card-1",
                Name = "OnePay Card", Type = "Credit", CurrentBalance = 72.14m,
                ManualCreditLimit = 1000m, ManualNextPaymentDueDate = today.AddDays(20),
            });
        });

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>(
            "/api/cashflow?pastDays=0&futureDays=45", JsonDefaults.Options);

        Assert.Contains(response!.Entries, e => e.Description == "OnePay Card - Fatura" && e.Amount == -72.14m);
    }

    [Fact]
    public async Task DownloadReport_returns_a_csv_with_the_same_rows_as_the_json_response()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "checking",
                Name = "Checking", Type = "Depository", CurrentBalance = 1000m,
            });
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t1", AccountId = "checking",
                ItemId = "item-1", Amount = 50m, Date = today, Name = "Grocery Store", Pending = false,
            });
        });

        var response = await _client.GetAsync("/api/cashflow/report");
        response.EnsureSuccessStatusCode();

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        var csv = System.Text.Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync());

        Assert.Contains("Data,Descricao,SaldoAntes,Valor,SaldoDepois,Situacao", csv);
        // Saldo atual 1000 já reflete a compra de 50: antes era 1050, depois é 1000.
        Assert.Contains("Grocery Store,1050.00,-50.00,1000.00,Done", csv);
    }

    [Fact]
    public async Task GetCashFlow_chains_balance_before_and_after_line_by_line()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "checking",
                Name = "Checking", Type = "Depository", CurrentBalance = 1000m,
            });
            // Plaid: positivo = saída. Salário (-2000) e compra (100): 1000 hoje => começou em -900.
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t1", AccountId = "checking",
                ItemId = "item-1", Amount = -2000m, Date = today.AddDays(-3), Name = "Payroll",
            });
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t2", AccountId = "checking",
                ItemId = "item-1", Amount = 100m, Date = today.AddDays(-1), Name = "Groceries",
            });
        });

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>("/api/cashflow", JsonDefaults.Options);

        var payroll = response!.Entries[0];
        var groceries = response.Entries[1];
        Assert.Equal(-900m, response.StartingBalance);
        Assert.Equal(-900m, payroll.BalanceBefore);
        Assert.Equal(1100m, payroll.Balance);
        Assert.Equal(payroll.Balance, groceries.BalanceBefore);
        Assert.Equal(1000m, groceries.Balance);
        Assert.All(response.Entries, e => Assert.Equal(e.BalanceBefore + e.Amount, e.Balance));
    }

    [Fact]
    public async Task GetCashFlow_ignores_credit_card_purchases_because_they_do_not_touch_the_checking_balance()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-1", AccountId = "checking",
                Name = "Checking", Type = "Depository", CurrentBalance = 1000m,
            });
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "item-2", AccountId = "card",
                Name = "Card", Type = "Credit", CurrentBalance = 300m,
            });
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "t-card", AccountId = "card",
                ItemId = "item-2", Amount = 300m, Date = today.AddDays(-1), Name = "Card purchase",
            });
        });

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>("/api/cashflow", JsonDefaults.Options);

        Assert.DoesNotContain(response!.Entries, e => e.Description == "Card purchase");
        Assert.Equal(1000m, response.StartingBalance);
    }
}