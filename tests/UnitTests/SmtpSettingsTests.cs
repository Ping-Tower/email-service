using Infrastructure.EmailManager;
using MailKit.Security;
using Xunit;
namespace UnitTests;
public class SmtpSettingsTests
{
    [Theory]
    [InlineData("starttls", SecureSocketOptions.StartTls)]
    [InlineData("STARTTLS", SecureSocketOptions.StartTls)]
    [InlineData("ssl", SecureSocketOptions.SslOnConnect)]
    [InlineData("tls", SecureSocketOptions.SslOnConnect)]
    public void Security_UsesEncryptedConnections(string security, SecureSocketOptions expected)
    {
        Assert.Equal(expected, new SmtpSettings { Security = security }.GetSocketOptions());
    }
    [Fact]
    public void Security_RejectsPlaintextFallback()
    {
        Assert.Throws<InvalidOperationException>(() => new SmtpSettings { Security = "none" }.GetSocketOptions());
    }
}
