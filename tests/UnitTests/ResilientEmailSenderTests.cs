using Domain;
using Infrastructure.EmailManager;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;
using Xunit;

namespace UnitTests;

public class ResilientEmailSenderTests
{
    [Fact]
    public async Task SendEmailAsync_WhenInnerSenderSucceeds_CallsInnerOnce()
    {
        var innerSenderCallCount = 0;
        var innerSender = new DelegatingEmailSender((_, _, _, _) =>
        {
            innerSenderCallCount++;
            return Task.CompletedTask;
        });
        var sender = new ResilientEmailSender(innerSender, NullLogger<ResilientEmailSender>.Instance);

        await sender.SendEmailAsync("user@example.com", "Subject", "<p>Body</p>", CancellationToken.None);

        Assert.Equal(1, innerSenderCallCount);
    }

    [Fact]
    public async Task SendEmailAsync_WhenTransientFailureOccurs_RetriesUntilSuccess()
    {
        var attemptCount = 0;
        var innerSender = new DelegatingEmailSender((_, _, _, _) =>
        {
            attemptCount++;
            if (attemptCount < 3)
                throw new IOException("temporary network failure");

            return Task.CompletedTask;
        });
        var sender = new ResilientEmailSender(innerSender, NullLogger<ResilientEmailSender>.Instance);

        await sender.SendEmailAsync("user@example.com", "Subject", "<p>Body</p>", CancellationToken.None);

        Assert.Equal(3, attemptCount);
    }

    [Fact]
    public async Task SendEmailAsync_WhenFiveNonRetryableFailuresOccur_OpensCircuitBreaker()
    {
        var innerSender = new DelegatingEmailSender((_, _, _, _) =>
            throw new InvalidOperationException("permanent failure"));
        var sender = new ResilientEmailSender(innerSender, NullLogger<ResilientEmailSender>.Instance);

        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sender.SendEmailAsync("user@example.com", "Subject", "<p>Body</p>", CancellationToken.None));
        }

        await Assert.ThrowsAsync<BrokenCircuitException>(() =>
            sender.SendEmailAsync("user@example.com", "Subject", "<p>Body</p>", CancellationToken.None));
    }

    private sealed class DelegatingEmailSender : IEmailSender
    {
        private readonly Func<string, string, string, CancellationToken, Task> _handler;

        public DelegatingEmailSender(Func<string, string, string, CancellationToken, Task> handler)
        {
            _handler = handler;
        }

        public Task SendEmailAsync(string email, string subject, string body, CancellationToken cancellationToken)
        {
            return _handler(email, subject, body, cancellationToken);
        }
    }
}
