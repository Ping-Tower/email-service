namespace EmailWorker;

public class RabbitMqSettings
{
    public string? HostName { get; set; }
    public int Port { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string MainQueue { get; set; } = "email.queue";
    public string DlxName { get; set; } = "email.dlx";
    public string DlqName { get; set; } = "email.dlq";
    public string RetryQueue { get; set; } = "email.retry";
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 5000;
}