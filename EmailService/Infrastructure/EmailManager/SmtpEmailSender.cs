using Domain;
using MimeKit;
using Polly;
using Microsoft.Extensions.Logging;
using Polly.Retry;
using Microsoft.Extensions.Options;

namespace Infrastructure.EmailManager;

public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpSettings _smtpSettings;
    public SmtpEmailSender(IOptions<SmtpSettings> smtpSettings)
    {
        _smtpSettings = smtpSettings.Value;
    }
    public async Task SendEmailAsync(string email, string subject, string HtmlBody, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtpSettings.FromName ?? "", _smtpSettings.FromAddress ?? ""));
        message.To.Add(new MailboxAddress("", email));
        message.Subject = subject;

        var body = new BodyBuilder
        {
            HtmlBody = HtmlBody
        };
        message.Body = body.ToMessageBody();

        using (var client = new MailKit.Net.Smtp.SmtpClient())
        {
            await client.ConnectAsync(_smtpSettings.Host, _smtpSettings.Port, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(_smtpSettings.UserName ?? "", _smtpSettings.Password ?? "");
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
    }
}
