namespace Infrastructure.EmailManager;

public class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string Security { get; set; } = "starttls";
    public int TimeoutSeconds { get; set; } = 30;

    public MailKit.Security.SecureSocketOptions GetSocketOptions() => Security.ToLowerInvariant() switch
    {
        "starttls" => MailKit.Security.SecureSocketOptions.StartTls,
        "ssl" or "tls" => MailKit.Security.SecureSocketOptions.SslOnConnect,
        _ => throw new InvalidOperationException("SMTP security must be starttls, ssl or tls.")
    };
}