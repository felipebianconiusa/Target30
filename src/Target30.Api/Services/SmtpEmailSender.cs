using System.Net;
using System.Net.Mail;

namespace Target30.Api.Services;

// Envio de email via SMTP simples (ex.: Gmail com senha de app). Se "Notifications:Smtp:Host"
// não estiver configurado, o envio é pulado silenciosamente (só loga) — assim o app continua
// funcionando normalmente sem notificações por email até o usuário configurar as credenciais.
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string subject, string body)
    {
        var host = _configuration["Notifications:Smtp:Host"];
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogInformation("SMTP não configurado — pulando envio de email para {Email}: {Subject}", toEmail, subject);
            return;
        }

        var port = int.TryParse(_configuration["Notifications:Smtp:Port"], out var p) ? p : 587;
        var user = _configuration["Notifications:Smtp:User"];
        var password = _configuration["Notifications:Smtp:Password"];
        var from = _configuration["Notifications:Smtp:From"] ?? user;
        var enableSsl = !bool.TryParse(_configuration["Notifications:Smtp:EnableSsl"], out var ssl) || ssl;

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            Credentials = string.IsNullOrWhiteSpace(user) ? null : new NetworkCredential(user, password),
        };

        using var message = new MailMessage(from!, toEmail, subject, body);

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            // Um email que falha não deve travar o resto do sync/alertas de outros usuários.
            _logger.LogError(ex, "Falha ao enviar email para {Email}", toEmail);
        }
    }
}
