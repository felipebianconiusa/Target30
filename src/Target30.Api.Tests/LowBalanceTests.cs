using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class LowBalanceDetectorTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    private static CashFlowEntryDto Row(int daysFromToday, string description, decimal amount, decimal after) =>
        new(Today.AddDays(daysFromToday), description, amount, after, "Pending", after - amount);

    [Fact]
    public void Finds_the_first_future_entry_that_takes_the_balance_below_the_threshold()
    {
        var rows = new[]
        {
            Row(-2, "Past purchase", -50m, 1000m),
            Row(3, "Rent", -600m, 400m),
            Row(9, "Card - Fatura", -500m, -100m),
        };

        var warning = LowBalanceDetector.Find(rows, 1000m, 0m, Today);

        Assert.NotNull(warning);
        Assert.Equal(Today.AddDays(9), warning!.Date);
        Assert.Equal("Card - Fatura", warning.Description);
        Assert.Equal(-100m, warning.Balance);
        Assert.Equal(-100m, warning.MinimumBalance);
        Assert.False(warning.AlreadyBelow);
    }

    [Fact]
    public void Uses_the_configured_threshold_instead_of_zero()
    {
        var rows = new[] { Row(3, "Rent", -600m, 400m) };

        var warning = LowBalanceDetector.Find(rows, 1000m, 500m, Today);

        Assert.Equal("Rent", warning!.Description);
    }

    [Fact]
    public void Reports_when_the_balance_is_already_below_today()
    {
        var warning = LowBalanceDetector.Find([], 100m, 500m, Today);

        Assert.True(warning!.AlreadyBelow);
        Assert.Equal("now", warning.Key);
    }

    [Fact]
    public void Returns_null_when_the_balance_stays_above_the_threshold()
    {
        var rows = new[] { Row(3, "Rent", -600m, 400m) };

        Assert.Null(LowBalanceDetector.Find(rows, 1000m, 0m, Today));
    }

    [Fact]
    public void Ignores_entries_that_already_happened()
    {
        // O saldo negativo de ontem já está refletido no saldo atual; não é aviso.
        var rows = new[] { Row(-1, "Yesterday", -900m, -50m) };

        Assert.Null(LowBalanceDetector.Find(rows, 1000m, 0m, Today));
    }

    [Fact]
    public void The_key_changes_when_the_problem_changes()
    {
        var a = LowBalanceDetector.Find([Row(3, "Rent", -600m, -10m)], 100m, 0m, Today);
        var b = LowBalanceDetector.Find([Row(4, "Rent", -600m, -10m)], 100m, 0m, Today);

        Assert.NotEqual(a!.Key, b!.Key);
    }
}

public class LowBalanceEndpointTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public LowBalanceEndpointTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CashFlow_flags_a_future_bill_that_pushes_the_balance_below_zero()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "checking",
                Name = "Checking", Type = "Depository", CurrentBalance = 100m,
            });
            db.RecurringBills.Add(new RecurringBill
            {
                UserId = TestAuthHandler.TestUserId, Description = "Rent", Amount = 500m,
                DayOfMonth = today.AddDays(5).Day, IsActive = true,
            });
        });

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>("/api/cashflow", JsonDefaults.Options);

        Assert.Equal("Rent", response!.LowBalance!.Description);
        Assert.Equal(-400m, response.LowBalance.Balance);
    }

    [Fact]
    public async Task CashFlow_has_no_warning_when_the_balance_is_comfortable()
    {
        await _factory.SeedAsync(db => db.PlaidAccounts.Add(new PlaidAccount
        {
            UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "checking",
            Name = "Checking", Type = "Depository", CurrentBalance = 5000m,
        }));

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>("/api/cashflow", JsonDefaults.Options);

        Assert.Null(response!.LowBalance);
    }

    [Fact]
    public async Task Settings_persist_the_low_balance_threshold_and_never_accept_a_negative_one()
    {
        var current = await _client.GetFromJsonAsync<SettingsDto>("/api/settings", JsonDefaults.Options);

        var saved = await (await _client.PutAsJsonAsync("/api/settings", current! with { LowBalanceThreshold = 750m }, JsonDefaults.Options))
            .Content.ReadFromJsonAsync<SettingsDto>(JsonDefaults.Options);
        Assert.Equal(750m, saved!.LowBalanceThreshold);

        var clamped = await (await _client.PutAsJsonAsync("/api/settings", current with { LowBalanceThreshold = -5m }, JsonDefaults.Options))
            .Content.ReadFromJsonAsync<SettingsDto>(JsonDefaults.Options);
        Assert.Equal(0m, clamped!.LowBalanceThreshold);
    }

    [Fact]
    public async Task CashFlow_does_not_charge_the_card_twice_for_the_amount_paid_before_closing()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _factory.SeedAsync(db =>
        {
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "checking",
                Name = "Checking", Type = "Depository", CurrentBalance = 5000m,
            });
            // Limite 1000, meta 30% => paga 300 antes do fechamento; o resto (300) vence na fatura.
            db.PlaidAccounts.Add(new PlaidAccount
            {
                UserId = TestAuthHandler.TestUserId, ItemId = "i", AccountId = "card",
                Name = "Card", Type = "Credit", CurrentBalance = 600m, CreditLimit = 1000m,
                StatementClosingDay = today.AddDays(15).Day, ManualNextPaymentDueDate = today.AddDays(25),
            });
        });

        var response = await _client.GetFromJsonAsync<CashFlowResponseDto>("/api/cashflow?pastDays=0&futureDays=60", JsonDefaults.Options);

        var closing = Assert.Single(response!.Entries, e => e.Description == "Card - Fechamento");
        var invoice = Assert.Single(response.Entries, e => e.Description == "Card - Fatura");
        Assert.Equal(-300m, closing.Amount);
        Assert.Equal(-300m, invoice.Amount);
        Assert.Equal(-600m, closing.Amount + invoice.Amount);
    }
}
