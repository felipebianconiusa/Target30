namespace Target30.Api.Tests;

public class DataFreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_bank_updated_a_few_hours_ago_is_ok()
    {
        Assert.Equal(FreshnessStatus.Ok, DataFreshness.Evaluate(Now.AddHours(-11), null, null, Now));
    }

    [Fact]
    public void A_bank_not_updated_for_over_a_day_is_stale()
    {
        Assert.Equal(FreshnessStatus.Stale, DataFreshness.Evaluate(Now.AddHours(-25), null, null, Now));
    }

    [Fact]
    public void A_failed_attempt_after_the_last_success_only_matters_once_the_data_is_half_a_day_old()
    {
        Assert.Equal(FreshnessStatus.Ok, DataFreshness.Evaluate(Now.AddHours(-6), Now.AddHours(-1), null, Now));
        Assert.Equal(FreshnessStatus.Stale, DataFreshness.Evaluate(Now.AddHours(-13), Now.AddHours(-1), null, Now));
    }

    [Fact]
    public void An_old_failure_followed_by_a_success_is_ignored()
    {
        Assert.Equal(FreshnessStatus.Ok, DataFreshness.Evaluate(Now.AddHours(-13), Now.AddHours(-30), null, Now));
    }

    [Fact]
    public void A_connection_error_needs_attention_even_if_the_data_is_recent()
    {
        Assert.Equal(FreshnessStatus.NeedsAttention,
            DataFreshness.Evaluate(Now.AddHours(-1), null, "ITEM_LOGIN_REQUIRED", Now));
    }

    [Fact]
    public void Missing_information_is_not_treated_as_stale()
    {
        Assert.Equal(FreshnessStatus.Ok, DataFreshness.Evaluate(null, null, null, Now));
    }

    [Fact]
    public void Alerts_once_per_episode()
    {
        Assert.True(DataFreshness.ShouldAlert(FreshnessStatus.Stale, null));
        Assert.False(DataFreshness.ShouldAlert(FreshnessStatus.Stale, DateTime.UtcNow));
        Assert.False(DataFreshness.ShouldAlert(FreshnessStatus.Ok, null));
    }

    [Fact]
    public void The_alert_body_names_each_bank_and_how_long_it_has_been_stale()
    {
        var body = DataFreshness.BuildAlertBody(
        [
            ("Bank of America", FreshnessStatus.Stale, Now.AddHours(-30)),
            ("Citibank", FreshnessStatus.NeedsAttention, Now.AddHours(-1)),
        ], Now);

        Assert.Contains("Bank of America: o Plaid não atualiza há 30h", body);
        Assert.Contains("Citibank: a conexão precisa de atenção", body);
    }

    [Theory]
    [InlineData(FreshnessStatus.Ok, "ok")]
    [InlineData(FreshnessStatus.Stale, "stale")]
    [InlineData(FreshnessStatus.NeedsAttention, "attention")]
    public void Status_is_exposed_to_the_frontend_as_a_stable_string(FreshnessStatus status, string expected)
    {
        Assert.Equal(expected, status.ToApiString());
    }
}
