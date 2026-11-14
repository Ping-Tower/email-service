using Domain;
using Microsoft.Extensions.Options;
using MimeKit;

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
            client.Timeout = checked(_smtpSettings.TimeoutSeconds * 1000);
            await client.ConnectAsync(
                _smtpSettings.Host,
                _smtpSettings.Port,
                _smtpSettings.GetSocketOptions(),
                cancellationToken);
            await client.AuthenticateAsync(_smtpSettings.UserName ?? "", _smtpSettings.Password ?? "", cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
    }
}
