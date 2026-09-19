namespace Target30.Api.Tests;

public class HealthScoreCalculatorTests
{
    private static CardProjection WithUtilization(decimal utilization, decimal target) =>
        new(0m, 0m, utilization, target, 0m, null, null, null);

    [Fact]
    public void Compute_is_100_when_no_cards_have_a_limit_and_spending_did_not_increase()
    {
        var result = HealthScoreCalculator.Compute([], 100m, 100m);

        Assert.Equal(100, result.CardComponent);
        Assert.Equal(100, result.SpendingComponent);
        Assert.Equal(100, result.Score);
        Assert.Equal("great", result.Label);
    }

    [Fact]
    public void Compute_card_component_is_100_when_all_cards_are_within_target()
    {
        var cards = new[] { WithUtilization(20m, 30m), WithUtilization(10m, 30m) };

        var result = HealthScoreCalculator.Compute(cards, 100m, 100m);

        Assert.Equal(100, result.CardComponent);
    }

    [Fact]
    public void Compute_card_component_drops_as_utilization_exceeds_target()
    {
        // 10 pontos percentuais acima da meta * 2 = 20 de penalidade.
        var cards = new[] { WithUtilization(40m, 30m) };

        var result = HealthScoreCalculator.Compute(cards, 100m, 100m);

        Assert.Equal(80, result.CardComponent);
    }

    [Fact]
    public void Compute_card_component_floors_at_zero_for_very_high_utilization()
    {
        var cards = new[] { WithUtilization(100m, 10m) };

        var result = HealthScoreCalculator.Compute(cards, 100m, 100m);

        Assert.Equal(0, result.CardComponent);
    }

    [Fact]
    public void Compute_spending_component_is_100_when_there_is_no_previous_month_data()
    {
        var result = HealthScoreCalculator.Compute([], 500m, 0m);

        Assert.Equal(100, result.SpendingComponent);
    }

    [Fact]
    public void Compute_spending_component_is_100_when_spending_less_than_last_month()
    {
        var result = HealthScoreCalculator.Compute([], 80m, 100m);

        Assert.Equal(100, result.SpendingComponent);
    }

    [Fact]
    public void Compute_spending_component_drops_ten_points_per_ten_percent_increase()
    {
        // Gastou 20% a mais que o mês passado => -20 pontos.
        var result = HealthScoreCalculator.Compute([], 120m, 100m);

        Assert.Equal(80, result.SpendingComponent);
    }

    [Fact]
    public void Compute_overall_score_weighs_cards_60_percent_and_spending_40_percent()
    {
        var cards = new[] { WithUtilization(40m, 30m) }; // CardComponent = 80
        var result = HealthScoreCalculator.Compute(cards, 120m, 100m); // SpendingComponent = 80

        Assert.Equal(80, result.Score);
    }

    [Fact]
    public void Compute_label_is_great_when_everything_is_fine()
    {
        var result = HealthScoreCalculator.Compute([], 100m, 100m);
        Assert.Equal("great", result.Label);
    }

    [Fact]
    public void Compute_label_is_good_for_a_moderately_over_target_card()
    {
        // CardComponent = 100 - (60-30)*2 = 40; Score = 40*0.6 + 100*0.4 = 64.
        var cards = new[] { WithUtilization(60m, 30m) };
        var result = HealthScoreCalculator.Compute(cards, 100m, 100m);
        Assert.Equal("good", result.Label);
    }

    [Fact]
    public void Compute_label_is_fair_for_a_card_further_over_target()
    {
        // CardComponent = 100 - (70-30)*2 = 20; Score = 20*0.6 + 100*0.4 = 52.
        var cards = new[] { WithUtilization(70m, 30m) };
        var result = HealthScoreCalculator.Compute(cards, 100m, 100m);
        Assert.Equal("fair", result.Label);
    }

    [Fact]
    public void Compute_label_is_poor_when_maxed_out_card_and_spending_spike_combine()
    {
        // CardComponent = 0 (maxed penalty); spending +50% => SpendingComponent = 50.
        // Score = 0*0.6 + 50*0.4 = 20.
        var cards = new[] { WithUtilization(80m, 30m) };
        var result = HealthScoreCalculator.Compute(cards, 150m, 100m);
        Assert.Equal("poor", result.Label);
    }
}
