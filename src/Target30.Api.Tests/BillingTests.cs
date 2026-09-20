using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Target30.Api.Billing;
using Target30.Api.Controllers;
using Target30.Api.Data;
using Target30.Api.Models;

namespace Target30.Api.Tests;

public class SubscriptionAccessTests
{
    private static readonly DateTime Now = new(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly BillingOptions On = new() { Enabled = true, ExemptEmails = ["owner@x.com"] };

    private static Subscription Sub(string status, DateTime? trialEnds = null, DateTime? periodEnd = null) =>
        new() { UserId = "u", Status = status, TrialEndsAt = trialEnds, CurrentPeriodEnd = periodEnd };

    [Fact]
    public void Everyone_has_access_when_billing_is_off()
    {
        Assert.True(SubscriptionAccess.Evaluate(new BillingOptions(), null, "x@y.z", Now).HasAccess);
    }

    [Fact]
    public void The_owner_is_exempt_regardless_of_case_and_spaces()
    {
        Assert.Equal("exempt", SubscriptionAccess.Evaluate(On, null, " Owner@X.com ", Now).Reason);
    }

    [Fact]
    public void A_trial_gives_access_until_it_ends()
    {
        Assert.True(SubscriptionAccess.Evaluate(On, Sub("trial", trialEnds: Now.AddDays(3)), "a@b.c", Now).HasAccess);
        Assert.False(SubscriptionAccess.Evaluate(On, Sub("trial", trialEnds: Now.AddDays(-1)), "a@b.c", Now).HasAccess);
    }

    [Fact]
    public void An_active_subscription_has_access()
    {
        Assert.True(SubscriptionAccess.Evaluate(On, Sub("active"), "a@b.c", Now).HasAccess);
    }

    [Fact]
    public void Past_due_keeps_access_only_for_a_short_grace_period_after_the_period_end()
    {
        Assert.True(SubscriptionAccess.Evaluate(On, Sub("past_due", periodEnd: Now.AddDays(-2)), "a@b.c", Now).HasAccess);
        Assert.False(SubscriptionAccess.Evaluate(On, Sub("past_due", periodEnd: Now.AddDays(-4)), "a@b.c", Now).HasAccess);
        Assert.False(SubscriptionAccess.Evaluate(On, Sub("past_due"), "a@b.c", Now).HasAccess);
    }

    [Theory]
    [InlineData("canceled")]
    [InlineData("none")]
    public void Canceled_or_missing_subscriptions_have_no_access(string status)
    {
        Assert.False(SubscriptionAccess.Evaluate(On, Sub(status), "a@b.c", Now).HasAccess);
        Assert.False(SubscriptionAccess.Evaluate(On, null, "a@b.c", Now).HasAccess);
    }

    [Fact]
    public void A_new_trial_ends_after_the_configured_days()
    {
        var trial = SubscriptionAccess.NewTrial("u", new BillingOptions { TrialDays = 7 }, Now);

        Assert.Equal(Now.AddDays(7), trial.TrialEndsAt);
    }

    [Theory]
    [InlineData("active", "active")]
    [InlineData("trialing", "active")]
    [InlineData("past_due", "past_due")]
    [InlineData("unpaid", "past_due")]
    [InlineData("canceled", "canceled")]
    [InlineData("incomplete_expired", "canceled")]
    public void Stripe_statuses_map_to_ours(string stripe, string expected)
    {
        Assert.Equal(expected, BillingWebhookProcessor.MapStatus(stripe));
    }
}

public class StripeSignatureTests
{
    private const string Secret = "whsec_test";
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Accepts_a_correctly_signed_recent_payload()
    {
        var header = StripeSignature.BuildHeader("{}", Now.ToUnixTimeSeconds(), Secret);

        Assert.True(StripeSignature.Verify("{}", header, Secret, Now));
    }

    [Fact]
    public void Rejects_a_tampered_payload_or_the_wrong_secret()
    {
        var header = StripeSignature.BuildHeader("{}", Now.ToUnixTimeSeconds(), Secret);

        Assert.False(StripeSignature.Verify("{\"x\":1}", header, Secret, Now));
        Assert.False(StripeSignature.Verify("{}", header, "whsec_other", Now));
    }

    [Fact]
    public void Rejects_a_stale_timestamp_to_block_replays()
    {
        var header = StripeSignature.BuildHeader("{}", Now.AddMinutes(-10).ToUnixTimeSeconds(), Secret);

        Assert.False(StripeSignature.Verify("{}", header, Secret, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("t=abc,v1=def")]
    [InlineData("v1=abc")]
    public void Rejects_missing_or_malformed_headers(string? header)
    {
        Assert.False(StripeSignature.Verify("{}", header, Secret, Now));
    }

    [Fact]
    public void Accepts_when_any_of_several_v1_signatures_matches()
    {
        var t = Now.ToUnixTimeSeconds();
        var header = $"t={t},v1=deadbeef,v1={StripeSignature.Compute("{}", t, Secret)}";

        Assert.True(StripeSignature.Verify("{}", header, Secret, Now));
    }
}

public class FakeStripeGateway : IStripeGateway
{
    public string? LastCustomerId { get; private set; }
    public string? LastEmail { get; private set; }

    public void Reset() => (LastCustomerId, LastEmail) = (null, null);

    public Task<string> CreateCheckoutUrlAsync(string userId, string? email, string? customerId, string successUrl, string cancelUrl)
    {
        LastEmail = email;
        LastCustomerId = customerId;
        return Task.FromResult($"https://checkout.test/{userId}?ok={Uri.EscapeDataString(successUrl)}");
    }

    public Task<string> CreatePortalUrlAsync(string customerId, string returnUrl) =>
        Task.FromResult($"https://portal.test/{customerId}");
}

public class BillingEndpointTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private const string WebhookSecret = "whsec_endpoint";
    private readonly Target30WebApplicationFactory _factory;

    public BillingEndpointTests(Target30WebApplicationFactory factory) => _factory = factory;

    public Task InitializeAsync()
    {
        _factory.Stripe.Reset();
        return _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Mexe nas opções do host único da fábrica (um segundo host com WithWebHostBuilder tentaria
    // migrar o mesmo SQLite em memória de novo).
    private HttpClient Client(Action<BillingOptions>? tweak = null, bool billing = true)
    {
        var o = _factory.Billing;
        o.Enabled = billing;
        o.TrialDays = 14;
        o.MaxItemsPerUser = 2;
        o.ExemptEmails = [];
        o.Stripe.SecretKey = "sk_test_x";
        o.Stripe.PriceId = "price_x";
        o.Stripe.WebhookSecret = WebhookSecret;
        tweak?.Invoke(o);
        return _factory.CreateClient();
    }
    private Task SeedSubscription(string status, DateTime? trialEnds = null, DateTime? periodEnd = null, string? customer = null) =>
        _factory.SeedAsync(db => db.Subscriptions.Add(new Subscription
        {
            UserId = TestAuthHandler.TestUserId, Status = status, TrialEndsAt = trialEnds, CurrentPeriodEnd = periodEnd, StripeCustomerId = customer,
        }));

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    private static HttpRequestMessage Webhook(string json, string? signature = null, string secret = WebhookSecret)
    {
        var header = signature ?? StripeSignature.BuildHeader(json, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), secret);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = Body(json) };
        request.Headers.Add("Stripe-Signature", header);
        return request;
    }

    [Fact]
    public async Task Status_starts_a_trial_the_first_time_the_user_shows_up()
    {
        var status = await Client().GetFromJsonAsync<BillingStatusDto>("/api/billing/status", JsonDefaults.Options);

        Assert.True(status!.Enabled);
        Assert.True(status.HasAccess);
        Assert.Equal("trial", status.Reason);
        Assert.NotNull(status.TrialEndsAt);
        Assert.True(status.CheckoutAvailable);
    }

    [Fact]
    public async Task With_billing_off_everything_is_open_and_no_trial_is_created()
    {
        var status = await Client(billing: false).GetFromJsonAsync<BillingStatusDto>("/api/billing/status", JsonDefaults.Options);

        Assert.False(status!.Enabled);
        Assert.True(status.HasAccess);
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<Target30DbContext>().Subscriptions);
    }

    [Fact]
    public async Task The_exempt_owner_is_never_charged_and_gets_no_trial_row()
    {
        var client = Client(o => o.ExemptEmails = [TestAuthHandler.TestUserEmail]);

        var status = await client.GetFromJsonAsync<BillingStatusDto>("/api/billing/status", JsonDefaults.Options);

        Assert.Equal("exempt", status!.Reason);
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<Target30DbContext>().Subscriptions);
    }

    [Fact]
    public async Task Costly_plaid_actions_return_402_when_the_trial_is_over()
    {
        await SeedSubscription("trial", trialEnds: DateTime.UtcNow.AddDays(-1));
        var client = Client();

        var sync = await client.PostAsync("/api/plaid/sync", null);
        var link = await client.PostAsync("/api/plaid/link-token", null);

        Assert.Equal(HttpStatusCode.PaymentRequired, sync.StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, link.StatusCode);
        Assert.Contains("subscription_required", await sync.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reading_your_own_data_still_works_without_a_subscription()
    {
        await SeedSubscription("canceled");
        var client = Client();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/plaid/items")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/cards")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/backup/export")).StatusCode);
    }

    [Fact]
    public async Task Connecting_a_third_bank_is_refused_at_the_item_limit()
    {
        await SeedSubscription("active");
        await _factory.SeedAsync(db =>
        {
            db.PlaidItems.Add(new PlaidItem { UserId = TestAuthHandler.TestUserId, ItemId = "i1", AccessToken = "t1" });
            db.PlaidItems.Add(new PlaidItem { UserId = TestAuthHandler.TestUserId, ItemId = "i2", AccessToken = "t2" });
        });

        var response = await Client().PostAsync("/api/plaid/link-token", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("item_limit", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Checkout_returns_the_stripe_url_and_reuses_a_known_customer()
    {
        await SeedSubscription("canceled", customer: "cus_123");
        var gateway = _factory.Stripe;
        var client = Client();

        var response = await client.PostAsync("/api/billing/checkout", null);

        response.EnsureSuccessStatusCode();
        var url = (await response.Content.ReadFromJsonAsync<UrlDto>(JsonDefaults.Options))!.Url;
        Assert.StartsWith("https://checkout.test/", url);
        Assert.Equal("cus_123", gateway.LastCustomerId);
    }

    [Fact]
    public async Task Checkout_is_refused_when_already_subscribed_or_stripe_is_not_configured()
    {
        await SeedSubscription("active");
        Assert.Equal(HttpStatusCode.Conflict, (await Client().PostAsync("/api/billing/checkout", null)).StatusCode);

        var unconfigured = Client(o => o.Stripe.SecretKey = "");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await unconfigured.PostAsync("/api/billing/checkout", null)).StatusCode);
    }

    [Fact]
    public async Task Portal_needs_a_stripe_customer()
    {
        await SeedSubscription("trial", trialEnds: DateTime.UtcNow.AddDays(3));
        Assert.Equal(HttpStatusCode.NotFound, (await Client().PostAsync("/api/billing/portal", null)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Target30DbContext>();
            db.Subscriptions.Single().StripeCustomerId = "cus_9";
            await db.SaveChangesAsync();
        }
        var ok = await Client().PostAsync("/api/billing/portal", null);
        Assert.Contains("https://portal.test/cus_9", await ok.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Webhook_rejects_a_bad_signature_and_does_not_change_anything()
    {
        await SeedSubscription("trial", trialEnds: DateTime.UtcNow.AddDays(3));
        var json = """{"type":"checkout.session.completed","data":{"object":{"client_reference_id":"__USER__","customer":"cus_1","subscription":"sub_1"}}}""".Replace("__USER__", TestAuthHandler.TestUserId);

        var response = await Client().SendAsync(Webhook(json, signature: "t=1,v1=bad"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal("trial", scope.ServiceProvider.GetRequiredService<Target30DbContext>().Subscriptions.Single().Status);
    }

    [Fact]
    public async Task Webhook_checkout_completed_activates_the_subscription_and_links_the_customer()
    {
        var json = """{"type":"checkout.session.completed","data":{"object":{"client_reference_id":"__USER__","customer":"cus_1","subscription":"sub_1"}}}""".Replace("__USER__", TestAuthHandler.TestUserId);

        var response = await Client().SendAsync(Webhook(json));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var sub = scope.ServiceProvider.GetRequiredService<Target30DbContext>().Subscriptions.Single();
        Assert.Equal(("active", "cus_1", "sub_1"), (sub.Status, sub.StripeCustomerId, sub.StripeSubscriptionId));
    }

    [Fact]
    public async Task Webhook_subscription_updates_follow_the_stripe_status_and_period_end()
    {
        await SeedSubscription("active", customer: "cus_1");
        var periodEnd = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var pastDue = """{"type":"customer.subscription.updated","data":{"object":{"id":"sub_1","customer":"cus_1","status":"past_due","items":{"data":[{"current_period_end":__END__}]}}}}""".Replace("__END__", periodEnd.ToString());

        await Client().SendAsync(Webhook(pastDue));

        using (var scope = _factory.Services.CreateScope())
        {
            var sub = scope.ServiceProvider.GetRequiredService<Target30DbContext>().Subscriptions.Single();
            Assert.Equal("past_due", sub.Status);
            Assert.Equal(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc), sub.CurrentPeriodEnd);
        }

        var deleted = """{"type":"customer.subscription.deleted","data":{"object":{"id":"sub_1","customer":"cus_1","status":"canceled"}}}""";
        await Client().SendAsync(Webhook(deleted));

        using var scope2 = _factory.Services.CreateScope();
        Assert.Equal("canceled", scope2.ServiceProvider.GetRequiredService<Target30DbContext>().Subscriptions.Single().Status);
    }

    [Fact]
    public async Task Webhook_payment_failed_marks_past_due_and_unknown_events_are_acknowledged()
    {
        await SeedSubscription("active", customer: "cus_1");

        await Client().SendAsync(Webhook("""{"type":"invoice.payment_failed","data":{"object":{"customer":"cus_1"}}}"""));
        var unknown = await Client().SendAsync(Webhook("""{"type":"something.else","data":{"object":{}}}"""));

        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal("past_due", scope.ServiceProvider.GetRequiredService<Target30DbContext>().Subscriptions.Single().Status);
    }

    [Fact]
    public async Task Webhook_is_unavailable_without_a_configured_secret()
    {
        var client = Client(o => o.Stripe.WebhookSecret = "");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(Webhook("{}"))).StatusCode);
    }

    private record UrlDto(string Url);
}
