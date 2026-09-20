namespace Target30.Api.Tests;

public class PlaidRefreshGuardTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Allows_the_first_refresh()
    {
        Assert.Null(PlaidRefreshGuard.RemainingCooldown(null, Now));
    }

    [Fact]
    public void Blocks_a_second_refresh_inside_the_cooldown_and_says_how_long_to_wait()
    {
        var remaining = PlaidRefreshGuard.RemainingCooldown(Now.AddMinutes(-4), Now);

        Assert.Equal(TimeSpan.FromMinutes(6), remaining);
    }

    [Fact]
    public void Allows_again_once_the_cooldown_has_passed()
    {
        Assert.Null(PlaidRefreshGuard.RemainingCooldown(Now - PlaidRefreshGuard.Cooldown, Now));
        Assert.Null(PlaidRefreshGuard.RemainingCooldown(Now.AddHours(-3), Now));
    }
}
