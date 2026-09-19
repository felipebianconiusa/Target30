namespace Target30.Api.Services;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body);
}
