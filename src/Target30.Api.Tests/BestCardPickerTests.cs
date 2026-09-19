using Target30.Api.Models;

namespace Target30.Api.Tests;

public class BestCardPickerTests
{
    private static readonly DateOnly Today = new(2026, 1, 15);

    private static (PlaidAccount, CardProjection) Card(
        string name, DateOnly? closing, decimal balance = 100m, decimal limit = 1000m)
    {
        var account = new PlaidAccount
        {
            UserId = "u", ItemId = "i", AccountId = name, Name = name, Type = "Credit",
        };
        decimal? utilization = limit > 0 ? Math.Round(balance / limit * 100, 1) : null;
        var projection = new CardProjection(balance, limit, utilization, 30m, 0m, closing, closing, null);
        return (account, projection);
    }

    [Fact]
    public void Rank_prefers_the_card_that_takes_longest_to_close()
    {
        var (eligible, _) = BestCardPicker.Rank(
        [
            Card("closes-soon", new DateOnly(2026, 1, 18)),
            Card("closes-late", new DateOnly(2026, 2, 10)),
            Card("closes-mid", new DateOnly(2026, 1, 28)),
        ], Today);

        Assert.Equal(["closes-late", "closes-mid", "closes-soon"], eligible.Select(c => c.Account.Name));
        Assert.Equal(26, eligible[0].DaysUntilClosing);
    }

    [Fact]
    public void Rank_excludes_a_card_whose_limit_is_maxed_out_even_if_it_closes_last()
    {
        var (eligible, excluded) = BestCardPicker.Rank(
        [
            Card("maxed", new DateOnly(2026, 2, 10), balance: 1000m, limit: 1000m),
            Card("free", new DateOnly(2026, 1, 20)),
        ], Today);

        Assert.Equal("free", Assert.Single(eligible).Account.Name);
        var out1 = Assert.Single(excluded);
        Assert.Equal("maxed", out1.Account.Name);
        Assert.Equal(CardExclusionReason.LimitReached, out1.ExclusionReason);
    }

    [Fact]
    public void Rank_excludes_a_card_over_its_limit()
    {
        var (eligible, excluded) = BestCardPicker.Rank(
            [Card("over", new DateOnly(2026, 2, 10), balance: 1200m, limit: 1000m)], Today);

        Assert.Empty(eligible);
        Assert.Equal(CardExclusionReason.LimitReached, Assert.Single(excluded).ExclusionReason);
    }

    [Fact]
    public void Rank_keeps_a_card_with_unknown_limit_as_eligible()
    {
        var (eligible, excluded) = BestCardPicker.Rank(
            [Card("no-limit-info", new DateOnly(2026, 1, 25), balance: 2861m, limit: 0m)], Today);

        var card = Assert.Single(eligible);
        Assert.Null(card.AvailableCredit);
        Assert.Empty(excluded);
    }

    [Fact]
    public void Rank_excludes_a_card_without_a_closing_day_because_it_cannot_be_compared()
    {
        var (eligible, excluded) = BestCardPicker.Rank([Card("no-day", null)], Today);

        Assert.Empty(eligible);
        Assert.Equal(CardExclusionReason.NoClosingDay, Assert.Single(excluded).ExclusionReason);
    }

    [Fact]
    public void Rank_breaks_ties_by_lower_utilization()
    {
        var closing = new DateOnly(2026, 2, 1);
        var (eligible, _) = BestCardPicker.Rank(
        [
            Card("more-used", closing, balance: 500m),
            Card("less-used", closing, balance: 100m),
        ], Today);

        Assert.Equal("less-used", eligible[0].Account.Name);
    }

    [Fact]
    public void Rank_returns_nothing_when_there_are_no_cards()
    {
        var (eligible, excluded) = BestCardPicker.Rank([], Today);
        Assert.Empty(eligible);
        Assert.Empty(excluded);
    }

    [Fact]
    public void Rank_classifies_far_away_cards_within_target_as_recommended()
    {
        // 26 dias até fechar (>= 15), utilização 10% (dentro da meta de 30%).
        var (eligible, _) = BestCardPicker.Rank([Card("far", new DateOnly(2026, 2, 10))], Today);

        Assert.Equal(CardTier.Recommended, eligible[0].Tier);
        Assert.False(eligible[0].OverTarget);
    }

    [Theory]
    [InlineData(14, CardTier.Alternative)]
    [InlineData(7, CardTier.Alternative)]
    [InlineData(6, CardTier.Caution)]
    [InlineData(1, CardTier.Caution)]
    [InlineData(15, CardTier.Recommended)]
    public void Rank_tier_depends_on_days_until_closing(int days, CardTier expected)
    {
        var (eligible, _) = BestCardPicker.Rank([Card("c", Today.AddDays(days))], Today);

        Assert.Equal(expected, eligible[0].Tier);
    }

    [Fact]
    public void Rank_puts_a_card_over_its_utilization_target_in_the_caution_group_even_if_it_closes_last()
    {
        // 50% de utilização com meta de 30%.
        var (eligible, _) = BestCardPicker.Rank(
        [
            Card("over-target", new DateOnly(2026, 2, 20), balance: 500m),
            Card("ok-but-sooner", new DateOnly(2026, 1, 25), balance: 100m),
        ], Today);

        Assert.Equal("ok-but-sooner", eligible[0].Account.Name);
        Assert.Equal(CardTier.Alternative, eligible[0].Tier);
        Assert.Equal(CardTier.Caution, eligible[1].Tier);
        Assert.True(eligible[1].OverTarget);
    }

    [Fact]
    public void Rank_does_not_treat_an_unknown_utilization_as_over_target()
    {
        var (eligible, _) = BestCardPicker.Rank([Card("no-limit", new DateOnly(2026, 2, 10), limit: 0m)], Today);

        Assert.Equal(CardTier.Recommended, eligible[0].Tier);
    }

    [Fact]
    public void Rank_orders_groups_recommended_then_alternative_then_caution()
    {
        var (eligible, _) = BestCardPicker.Rank(
        [
            Card("caution", Today.AddDays(3)),
            Card("recommended", Today.AddDays(20)),
            Card("alternative", Today.AddDays(10)),
        ], Today);

        Assert.Equal(["recommended", "alternative", "caution"], eligible.Select(c => c.Account.Name));
    }
}