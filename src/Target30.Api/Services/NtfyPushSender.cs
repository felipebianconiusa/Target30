using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace Target30.Api.Services;

public interface IPushSender
{
    Task SendAsync(string topic, string title, string message);
}

// Notificação no celular via ntfy (https://ntfy.sh — sem conta): o usuário escolhe um tópico
// difícil de adivinhar, assina no app do ntfy e a API publica nele. O tópico funciona como uma
// senha de leitura: por isso o mostramos só ao dono e validamos o formato.
public partial class NtfyPushSender : IPushSender
{
    private readonly HttpClient _http;
    private readonly string _server;

    public NtfyPushSender(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _server = (configuration["Push:NtfyServer"] is { Length: > 0 } s ? s : "https://ntfy.sh").TrimEnd('/');
    }

    public static bool IsValidTopic(string? topic) => topic is not null && TopicRegex().IsMatch(topic);

    public async Task SendAsync(string topic, string title, string message)
    {
        if (!IsValidTopic(topic))
            throw new ArgumentException("Tópico inválido.", nameof(topic));

        // ntfy limita a mensagem a ~4 KB.
        var body = message.Length > 3500 ? message[..3500] + "…" : message;
        using var response = await _http.PostAsJsonAsync(_server, new { topic, title, message = body, priority = 3, tags = new[] { "moneybag" } });
        response.EnsureSuccessStatusCode();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{6,64}$")]
    private static partial Regex TopicRegex();
}
