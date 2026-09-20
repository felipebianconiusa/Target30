using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Target30.Api.Controllers;
using Target30.Api.Data;

namespace Target30.Api.Tests;

public class WaitlistTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public WaitlistTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync()
    {
        _factory.Billing.ExemptEmails = [];
        return _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<HttpResponseMessage> Join(string? email, string? note = null, string? website = null) =>
        _client.PostAsJsonAsync("/api/waitlist", new WaitlistRequest(email, note, "pt", website), JsonDefaults.Options);

    private int Count()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<Target30DbContext>().WaitlistEntries.Count();
    }

    [Theory]
    [InlineData("a@b.co", "a@b.co")]
    [InlineData("  Ana@Example.COM ", "ana@example.com")]
    public void NormalizeEmail_accepts_simple_addresses_in_lowercase(string raw, string expected)
    {
        Assert.Equal(expected, WaitlistController.NormalizeEmail(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("a@b")]
    [InlineData("Ana <ana@example.com>")]
    [InlineData("a@b.co, c@d.co")]
    public void NormalizeEmail_rejects_anything_else(string? raw)
    {
        Assert.Null(WaitlistController.NormalizeEmail(raw));
    }

    [Fact]
    public async Task Join_stores_a_normalized_entry_and_is_idempotent()
    {
        Assert.Equal(HttpStatusCode.OK, (await Join("Ana@Example.com", "quero usar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Join("ana@example.com")).StatusCode);

        Assert.Equal(1, Count());
    }

    [Fact]
    public async Task Join_rejects_an_invalid_email()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Join("nope")).StatusCode);
        Assert.Equal(0, Count());
    }

    [Fact]
    public async Task Join_silently_drops_bots_that_fill_the_honeypot()
    {
        var response = await Join("bot@example.com", website: "http://spam.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, Count());
    }

    [Fact]
    public async Task Join_truncates_a_long_note()
    {
        await Join("ana@example.com", new string('x', 1000));

        using var scope = _factory.Services.CreateScope();
        Assert.Equal(300, scope.ServiceProvider.GetRequiredService<Target30DbContext>().WaitlistEntries.Single().Note!.Length);
    }

    [Fact]
    public async Task Only_the_owner_can_read_the_list()
    {
        await Join("ana@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/waitlist")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync("/api/waitlist/csv")).StatusCode);

        _factory.Billing.ExemptEmails = [TestAuthHandler.TestUserEmail];
        var list = await _client.GetFromJsonAsync<List<WaitlistEntryDto>>("/api/waitlist", JsonDefaults.Options);
        Assert.Equal("ana@example.com", Assert.Single(list!).Email);
    }

    [Fact]
    public async Task The_csv_neutralizes_spreadsheet_formulas_in_free_text()
    {
        _factory.Billing.ExemptEmails = [TestAuthHandler.TestUserEmail];
        await Join("ana@example.com", "=HYPERLINK(\"http://evil\")");

        var csv = await (await _client.GetAsync("/api/waitlist/csv")).Content.ReadAsStringAsync();

        Assert.Contains("ana@example.com", csv);
        Assert.Contains("'=HYPERLINK", csv);
        Assert.DoesNotContain(",=HYPERLINK", csv);
    }
}
