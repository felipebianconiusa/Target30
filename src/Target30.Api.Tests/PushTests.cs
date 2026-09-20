using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Target30.Api.Controllers;
using Target30.Api.Models;
using Target30.Api.Services;

namespace Target30.Api.Tests;

public class FakePushSender : IPushSender
{
    public List<(string Topic, string Title, string Message)> Sent { get; } = [];
    public bool Fail { get; set; }

    public Task SendAsync(string topic, string title, string message)
    {
        if (Fail)
            throw new HttpRequestException("push down");
        Sent.Add((topic, title, message));
        return Task.CompletedTask;
    }

    public void Reset()
    {
        Sent.Clear();
        Fail = false;
    }
}

public class FakeEmailSender : IEmailSender
{
    public List<(string To, string Subject)> Sent { get; } = [];
    public bool Fail { get; set; }

    public Task SendAsync(string toEmail, string subject, string body)
    {
        if (Fail)
            throw new InvalidOperationException("smtp down");
        Sent.Add((toEmail, subject));
        return Task.CompletedTask;
    }
}

public class AlertDispatcherTests
{
    private static (AlertDispatcher Dispatcher, FakeEmailSender Email, FakePushSender Push) Build()
    {
        var email = new FakeEmailSender();
        var push = new FakePushSender();
        return (new AlertDispatcher(email, push, NullLogger<AlertDispatcher>.Instance), email, push);
    }

    private static UserSettings Settings(string? email, string? topic) => new() { UserId = "u", Email = email, PushTopic = topic };

    [Fact]
    public async Task Sends_to_both_channels_when_both_are_configured()
    {
        var (d, email, push) = Build();

        await d.SendAsync(Settings("a@b.co", "my-topic-123"), "Subject", "Body");

        Assert.Equal([("a@b.co", "Subject")], email.Sent);
        Assert.Equal([("my-topic-123", "Subject", "Body")], push.Sent);
    }

    [Fact]
    public async Task Uses_only_the_channels_that_are_set_up()
    {
        var (d, email, push) = Build();

        await d.SendAsync(Settings("a@b.co", null), "S", "B");
        await d.SendAsync(Settings(null, "my-topic-123"), "S", "B");

        Assert.Single(email.Sent);
        Assert.Single(push.Sent);
    }

    [Fact]
    public async Task One_channel_failing_does_not_stop_the_other_nor_raise()
    {
        var (d, email, push) = Build();
        email.Fail = true;

        await d.SendAsync(Settings("a@b.co", "my-topic-123"), "S", "B");

        Assert.Single(push.Sent);
    }

    [Fact]
    public async Task Raises_only_when_every_configured_channel_fails()
    {
        var (d, email, push) = Build();
        email.Fail = true;
        push.Fail = true;

        await Assert.ThrowsAsync<AggregateException>(() => d.SendAsync(Settings("a@b.co", "my-topic-123"), "S", "B"));
        email.Fail = false;
        await d.SendAsync(Settings("a@b.co", "my-topic-123"), "S", "B"); // um canal ok = sem exceção
    }

    [Fact]
    public async Task Does_nothing_and_does_not_raise_without_any_channel()
    {
        var (d, email, push) = Build();

        await d.SendAsync(Settings(null, null), "S", "B");

        Assert.Empty(email.Sent);
        Assert.Empty(push.Sent);
        Assert.False(AlertDispatcher.HasChannel(Settings(" ", "")));
    }

    [Theory]
    [InlineData("target30-abc123", true)]
    [InlineData("a_b-C9d8e7", true)]
    [InlineData("short", false)]
    [InlineData("has space here", false)]
    [InlineData("com/barra-12345", false)]
    [InlineData("", false)]
    public void Topic_format_is_validated(string topic, bool valid)
    {
        Assert.Equal(valid, NtfyPushSender.IsValidTopic(topic));
    }
}

public class PushSettingsEndpointTests : IClassFixture<Target30WebApplicationFactory>, IAsyncLifetime
{
    private readonly Target30WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PushSettingsEndpointTests(Target30WebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync()
    {
        _factory.Push.Reset();
        return _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpResponseMessage> Save(string? topic)
    {
        var current = await _client.GetFromJsonAsync<SettingsDto>("/api/settings", JsonDefaults.Options);
        return await _client.PutAsJsonAsync("/api/settings", current! with { PushTopic = topic }, JsonDefaults.Options);
    }

    [Fact]
    public async Task Saves_a_valid_topic_trimmed_and_clears_it_when_empty()
    {
        var saved = await (await Save("  my-secret-topic  ")).Content.ReadFromJsonAsync<SettingsDto>(JsonDefaults.Options);
        Assert.Equal("my-secret-topic", saved!.PushTopic);

        var cleared = await (await Save("")).Content.ReadFromJsonAsync<SettingsDto>(JsonDefaults.Options);
        Assert.Null(cleared!.PushTopic);
    }

    [Fact]
    public async Task Rejects_an_invalid_topic_without_saving_it()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Save("bad topic!")).StatusCode);

        var settings = await _client.GetFromJsonAsync<SettingsDto>("/api/settings", JsonDefaults.Options);
        Assert.Null(settings!.PushTopic);
    }

    [Fact]
    public async Task The_test_button_needs_a_topic_and_sends_to_it()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/settings/push-test", null)).StatusCode);

        await Save("my-secret-topic");
        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsync("/api/settings/push-test", null)).StatusCode);

        Assert.Equal("my-secret-topic", Assert.Single(_factory.Push.Sent).Topic);
    }

    [Fact]
    public async Task The_test_button_reports_a_gateway_error_when_the_push_server_fails()
    {
        await Save("my-secret-topic");
        _factory.Push.Fail = true;

        Assert.Equal(HttpStatusCode.BadGateway, (await _client.PostAsync("/api/settings/push-test", null)).StatusCode);
    }
}
