using System.Net.Http.Json;
using Target30.Api.Controllers;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class BillsControllerTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public BillsControllerTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static void AddMonthlyCharges(
        Target30.Api.Data.Target30DbContext db, string merchant, decimal amount, DateOnly start, int months)
    {
        for (var i = 0; i < months; i++)
        {
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId,
                PlaidTransactionId = $"{merchant}-{i}",
                AccountId = "acc-1",
                ItemId = "item-1",
                Amount = amount,
                Date = start.AddMonths(i),
                Name = merchant,
                MerchantName = merchant,
                Pending = false,
            });
        }
    }

    [Fact]
    public async Task DetectedSubscriptions_finds_a_merchant_charged_about_monthly_with_a_consistent_amount()
    {
        await _factory.SeedAsync(db =>
            AddMonthlyCharges(db, "Netflix", 15.99m, new DateOnly(2026, 1, 5), 3));

        var subscriptions = await _client.GetFromJsonAsync<List<DetectedSubscriptionDto>>(
            "/api/bills/detected-subscriptions", JsonDefaults.Options);

        var sub = Assert.Single(subscriptions!);
        Assert.Equal("Netflix", sub.MerchantName);
        Assert.Equal(15.99m, sub.AverageAmount);
        Assert.Equal(3, sub.Occurrences);
    }

    [Fact]
    public async Task DetectedSubscriptions_ignores_charges_with_irregular_intervals()
    {
        await _factory.SeedAsync(db =>
        {
            // Compras no mesmo mercado, mas com poucos dias de intervalo — não é assinatura.
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "g1", AccountId = "acc-1",
                ItemId = "item-1", Amount = 40m, Date = new DateOnly(2026, 1, 1), Name = "Grocery Store",
            });
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "g2", AccountId = "acc-1",
                ItemId = "item-1", Amount = 55m, Date = new DateOnly(2026, 1, 5), Name = "Grocery Store",
            });
        });

        var subscriptions = await _client.GetFromJsonAsync<List<DetectedSubscriptionDto>>(
            "/api/bills/detected-subscriptions", JsonDefaults.Options);

        Assert.Empty(subscriptions!);
    }

    [Fact]
    public async Task DetectedSubscriptions_ignores_charges_with_inconsistent_amounts()
    {
        await _factory.SeedAsync(db =>
        {
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "v1", AccountId = "acc-1",
                ItemId = "item-1", Amount = 20m, Date = new DateOnly(2026, 1, 1), Name = "Variable Store",
            });
            db.PlaidTransactions.Add(new PlaidTransaction
            {
                UserId = TestAuthHandler.TestUserId, PlaidTransactionId = "v2", AccountId = "acc-1",
                ItemId = "item-1", Amount = 90m, Date = new DateOnly(2026, 2, 1), Name = "Variable Store",
            });
        });

        var subscriptions = await _client.GetFromJsonAsync<List<DetectedSubscriptionDto>>(
            "/api/bills/detected-subscriptions", JsonDefaults.Options);

        Assert.Empty(subscriptions!);
    }

    [Fact]
    public async Task DetectedSubscriptions_excludes_a_merchant_already_registered_as_a_recurring_bill()
    {
        await _factory.SeedAsync(db =>
        {
            AddMonthlyCharges(db, "Spotify", 11.99m, new DateOnly(2026, 1, 5), 3);
            db.RecurringBills.Add(new RecurringBill
            {
                UserId = TestAuthHandler.TestUserId, Description = "Spotify", Amount = 11.99m,
                DayOfMonth = 5, IsActive = true,
            });
        });

        var subscriptions = await _client.GetFromJsonAsync<List<DetectedSubscriptionDto>>(
            "/api/bills/detected-subscriptions", JsonDefaults.Options);

        Assert.Empty(subscriptions!);
    }

    [Fact]
    public async Task DetectedSubscriptions_surfaces_a_price_change_for_an_existing_bill()
    {
        int billId = 0;
        await _factory.SeedAsync(db =>
        {
            AddMonthlyCharges(db, "Netflix", 17.99m, new DateOnly(2026, 1, 14), 3);
            var bill = new RecurringBill
            {
                UserId = TestAuthHandler.TestUserId, Description = "Netflix", Amount = 15.99m,
                DayOfMonth = 14, IsActive = true,
            };
            db.RecurringBills.Add(bill);
            db.SaveChanges();
            billId = bill.Id;
        });

        var subscriptions = await _client.GetFromJsonAsync<List<DetectedSubscriptionDto>>(
            "/api/bills/detected-subscriptions", JsonDefaults.Options);

        var sub = Assert.Single(subscriptions!);
        Assert.True(sub.IsPriceChange);
        Assert.Equal(15.99m, sub.PreviousAmount);
        Assert.Equal(17.99m, sub.AverageAmount);
        Assert.Equal(billId, sub.ExistingBillId);
    }
}
