using Target30.Api.Models;

namespace Target30.Api.Services;

// Único ponto de saída dos alertas: e-mail e/ou notificação no celular, conforme o que o usuário
// configurou. Um canal falhar não impede o outro; só falha de verdade se TODOS os canais falharem.
public class AlertDispatcher
{
    private readonly IEmailSender _email;
    private readonly IPushSender _push;
    private readonly ILogger<AlertDispatcher> _logger;

    public AlertDispatcher(IEmailSender email, IPushSender push, ILogger<AlertDispatcher> logger)
    {
        _email = email;
        _push = push;
        _logger = logger;
    }

    public static bool HasChannel(UserSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.Email) || !string.IsNullOrWhiteSpace(settings.PushTopic);

    public async Task SendAsync(UserSettings settings, string subject, string body)
    {
        var failures = new List<Exception>();
        var attempted = 0;

        if (!string.IsNullOrWhiteSpace(settings.Email))
        {
            attempted++;
            try
            {
                await _email.SendAsync(settings.Email!, subject, body);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
                _logger.LogWarning(ex, "Falha ao enviar e-mail de alerta");
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.PushTopic))
        {
            attempted++;
            try
            {
                await _push.SendAsync(settings.PushTopic!, subject, body);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
                _logger.LogWarning(ex, "Falha ao enviar notificação push");
            }
        }

        if (attempted > 0 && failures.Count == attempted)
            throw new AggregateException("Nenhum canal de alerta conseguiu enviar.", failures);
    }
}
