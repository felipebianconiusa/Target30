namespace Target30.Api.Tests;

public class WeeklyDigestTests
{
    [Fact]
    public void ShouldSend_is_true_when_never_sent_before()
    {
        Assert.True(WeeklyDigest.ShouldSend(lastSentDate: null, today: new DateOnly(2026, 1, 15)));
    }

    [Fact]
    public void ShouldSend_is_false_when_sent_less_than_the_interval_ago()
    {
        var today = new DateOnly(2026, 1, 15);
        var lastSent = today.AddDays(-3);

        Assert.False(WeeklyDigest.ShouldSend(lastSent, today, intervalDays: 7));
    }

    [Fact]
    public void ShouldSend_is_true_when_sent_exactly_the_interval_ago()
    {
        var today = new DateOnly(2026, 1, 15);
        var lastSent = today.AddDays(-7);

        Assert.True(WeeklyDigest.ShouldSend(lastSent, today, intervalDays: 7));
    }

    [Fact]
    public void ShouldSend_is_true_when_sent_more_than_the_interval_ago()
    {
        var today = new DateOnly(2026, 1, 15);
        var lastSent = today.AddDays(-30);

        Assert.True(WeeklyDigest.ShouldSend(lastSent, today, intervalDays: 7));
    }
}
