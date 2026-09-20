namespace Target30.Api.Tests;

public class AllowedEmailsTests
{
    [Fact]
    public void Anyone_can_log_in_when_no_list_is_configured()
    {
        Assert.True(AllowedEmails.IsAllowed(null, "x@y.z", true));
        Assert.True(AllowedEmails.IsAllowed([], "x@y.z", false));
        Assert.True(AllowedEmails.IsAllowed(["", "  "], "x@y.z", true));
    }

    [Fact]
    public void Only_listed_emails_get_in_when_a_list_is_configured()
    {
        string[] allowed = ["me@gmail.com"];

        Assert.True(AllowedEmails.IsAllowed(allowed, "me@gmail.com", true));
        Assert.False(AllowedEmails.IsAllowed(allowed, "stranger@gmail.com", true));
    }

    [Fact]
    public void The_comparison_ignores_case_and_surrounding_spaces()
    {
        Assert.True(AllowedEmails.IsAllowed([" Me@Gmail.com "], "me@gmail.COM", true));
    }

    [Fact]
    public void A_listed_email_that_google_has_not_verified_is_rejected()
    {
        Assert.False(AllowedEmails.IsAllowed(["me@gmail.com"], "me@gmail.com", false));
    }

    [Fact]
    public void A_missing_email_is_rejected_when_a_list_is_configured()
    {
        Assert.False(AllowedEmails.IsAllowed(["me@gmail.com"], null, true));
    }
}
