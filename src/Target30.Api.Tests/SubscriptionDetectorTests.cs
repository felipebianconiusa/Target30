using Target30.Api.Models;

namespace Target30.Api.Tests;

public class SubscriptionDetectorTests
{
    private static PlaidTransaction Tx(decimal amount, DateOnly date, string name = "Netflix") => new()
    {
        UserId = "u", PlaidTransactionId = Guid.NewGuid().ToString(), AccountId = "a", ItemId = "i",
        Amount = amount, Date = date, Name = name, MerchantName = name,
    };

    [Fact]
    public void Detect_returns_null_with_fewer_than_two_transactions()
    {
        var result = SubscriptionDetector.Detect([Tx(10m, new DateOnly(2026, 1, 1))]);
        Assert.Null(result);
    }

    [Fact]
    public void Detect_finds_a_consistent_monthly_charge()
    {
        var txs = new[]
        {
            Tx(15.99m, new DateOnly(2026, 1, 5)),
            Tx(15.99m, new DateOnly(2026, 2, 5)),
            Tx(15.99m, new DateOnly(2026, 3, 5)),
        };

        var result = SubscriptionDetector.Detect(txs);

        Assert.NotNull(result);
        Assert.Equal("Netflix", result!.MerchantName);
        Assert.Equal(15.99m, result.AverageAmount);
        Assert.Equal(5, result.SuggestedDayOfMonth);
        Assert.Equal(3, result.Occurrences);
        Assert.Equal(new DateOnly(2026, 3, 5), result.LastDate);
    }

    [Fact]
    public void Detect_returns_null_when_amounts_vary_too_much()
    {
        var txs = new[]
        {
            Tx(20m, new DateOnly(2026, 1, 1)),
            Tx(90m, new DateOnly(2026, 2, 1)),
        };

        Assert.Null(SubscriptionDetector.Detect(txs));
    }

    [Fact]
    public void Detect_returns_null_when_gaps_are_too_short()
    {
        var txs = new[]
        {
            Tx(40m, new DateOnly(2026, 1, 1)),
            Tx(55m, new DateOnly(2026, 1, 5)),
        };

        Assert.Null(SubscriptionDetector.Detect(txs));
    }

    [Fact]
    public void Detect_returns_null_when_gaps_are_too_long()
    {
        var txs = new[]
        {
            Tx(40m, new DateOnly(2026, 1, 1)),
            Tx(42m, new DateOnly(2026, 4, 1)),
        };

        Assert.Null(SubscriptionDetector.Detect(txs));
    }

    [Fact]
    public void Detect_tolerates_small_amount_variation()
    {
        var txs = new[]
        {
            Tx(100m, new DateOnly(2026, 1, 1)),
            Tx(105m, new DateOnly(2026, 2, 1)),
        };

        Assert.NotNull(SubscriptionDetector.Detect(txs));
    }
}
