using Domain;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Polly;

namespace Infrastructure.EmailManager;

public class ResilientEmailSender : IEmailSender
{
    private readonly IEmailSender _innerEmailSender;
    private readonly AsyncRetryPolicy _asyncRetryPolicy;
    private readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy;
    private readonly AsyncTimeoutPolicy _timeoutPolicy;
    private readonly ILogger<ResilientEmailSender> _logger;

    public ResilientEmailSender(IEmailSender innerEmailSender, ILogger<ResilientEmailSender> logger)
    {
        _innerEmailSender = innerEmailSender;
        _logger = logger;

        _asyncRetryPolicy = Policy
            .Handle<MailKit.Net.Smtp.SmtpCommandException>()
            .Or<MailKit.Net.Smtp.SmtpProtocolException>()
            .Or<System.Net.Sockets.SocketException>()
            .Or<IOException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning($"Retry {retryCount} encountered an error: {exception.Message}. Waiting {timeSpan} before next retry.");
                });

        _timeoutPolicy = Policy.TimeoutAsync(TimeSpan.FromSeconds(10));

        _circuitBreakerPolicy = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromMinutes(2),
                onBreak: (exception, breakDelay) =>
                {
                    _logger.LogWarning($"Circuit breaker opened for {breakDelay.TotalSeconds} seconds due to: {exception.Message}");
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker reset.");
                });
    }

    public async Task SendEmailAsync(string email, string subject, string HtmlBody, CancellationToken cancellationToken)
    {
        var combinedPolicy = Policy.WrapAsync(_asyncRetryPolicy, _timeoutPolicy, _circuitBreakerPolicy);
        await combinedPolicy.ExecuteAsync(
            async ct => await _innerEmailSender.SendEmailAsync(email, subject, HtmlBody, ct),
            cancellationToken);
    }
}
