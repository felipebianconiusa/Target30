namespace Target30.Api.Tests;

public class SyncLimitsTests
{
    private static readonly DateTime Now = new(2026, 9, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Liabilities_are_fetched_the_first_time_and_once_the_wait_is_over()
    {
        Assert.True(LiabilitiesSchedule.ShouldFetch(null, Now));
        Assert.True(LiabilitiesSchedule.ShouldFetch(Now.AddMinutes(-1), Now));
        Assert.True(LiabilitiesSchedule.ShouldFetch(Now, Now));
    }

    [Fact]
    public void Liabilities_are_skipped_while_the_wait_lasts()
    {
        Assert.False(LiabilitiesSchedule.ShouldFetch(Now.AddHours(1), Now));
    }

    [Fact]
    public void After_a_success_the_next_call_waits_the_interval_and_after_a_failure_a_week()
    {
        Assert.Equal(Now.AddHours(72), LiabilitiesSchedule.NextAfterSuccess(Now, TimeSpan.FromHours(72)));
        Assert.Equal(Now.AddDays(7), LiabilitiesSchedule.NextAfterFailure(Now));
    }

    [Fact]
    public void A_full_day_of_six_hour_sync_cycles_makes_one_liabilities_call_per_bank_every_three_days()
    {
        var interval = TimeSpan.FromHours(72);
        DateTime? next = null;
        var calls = 0;
        // 4 ciclos por dia, 9 dias.
        for (var cycle = 0; cycle < 36; cycle++)
        {
            var now = Now.AddHours(6 * cycle);
            if (!LiabilitiesSchedule.ShouldFetch(next, now))
                continue;
            calls++;
            next = LiabilitiesSchedule.NextAfterSuccess(now, interval);
        }

        Assert.Equal(3, calls); // dias 0, 3 e 6
    }

    [Fact]
    public void A_bank_synced_a_moment_ago_is_not_synced_again_on_startup_but_the_regular_cycle_still_runs()
    {
        var interval = TimeSpan.FromHours(6);

        Assert.False(SyncSchedule.IsDue(Now.AddMinutes(-10), Now, interval));   // reinício logo depois
        Assert.True(SyncSchedule.IsDue(Now.AddHours(-6), Now, interval));       // ciclo normal
        Assert.True(SyncSchedule.IsDue(Now.AddMinutes(-(6 * 60 - 1)), Now, interval)); // ciclo com um pouco de folga
        Assert.True(SyncSchedule.IsDue(null, Now, interval));                   // nunca sincronizou
    }
}
