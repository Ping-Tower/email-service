using Infrastructure.EmailManager;
using Xunit;

namespace IntegrationTests;

public class TemplateRenderingIntegrationTests
{
    [Fact]
    public async Task RenderAsync_ServerDownTemplate_RendersCopiedWorkerTemplate()
    {
        var renderer = new FileSystemEmailTemplateRenderer();

        var (subject, htmlBody) = await renderer.RenderAsync(
            "server-down",
            new Dictionary<string, string?>
            {
                ["serverName"] = "db-<primary>",
                ["host"] = "db.internal",
                ["timestampUtc"] = "2026-04-05T15:00:00Z",
                ["status"] = "DOWN"
            },
            CancellationToken.None);

        Assert.Equal("PingTower alert: db-<primary> is DOWN", subject.TrimEnd());
        Assert.Contains("db-&lt;primary&gt; is unavailable", htmlBody);
        Assert.Contains("db.internal", htmlBody);
        Assert.Contains("2026-04-05T15:00:00Z", htmlBody);
        Assert.Contains("Status: <strong>DOWN</strong>", htmlBody);
    }
    [Theory]
    [InlineData("registration-confirmation", "verificationUrl", "Confirm email")]
    [InlineData("password-reset", "resetUrl", "Reset password")]
    [InlineData("server-down", "serverUrl", "Open monitor")]
    [InlineData("server-up", "serverUrl", "Open monitor")]
    public async Task RenderAsync_AllTemplates_PreserveSafeLinksAndConsistentBranding(string template, string urlKey, string button)
    {
        var renderer = new FileSystemEmailTemplateRenderer();
        var (_, html) = await renderer.RenderAsync(template, new Dictionary<string, string?>
        {
            ["userName"] = "<Admin> & team", ["recipient"] = "<Admin> & team",
            ["serverName"] = "<Admin> & team", ["host"] = "db.internal",
            ["timestampUtc"] = "2026-11-14 20:00:00Z", ["status"] = "DOWN",
            [urlKey] = "https://pingtower.tech/app/servers/s1?one=1&two=2"
        }, CancellationToken.None);
        Assert.Contains("&lt;Admin&gt; &amp; team", html);
        Assert.DoesNotContain("<Admin>", html);
        Assert.Contains("href=\"https://pingtower.tech/app/servers/s1?one=1&amp;two=2\"", html);
        Assert.Contains(button, html);
        Assert.Contains("Four protocols. One clear picture.", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.DoesNotContain("{{", html);
        Assert.DoesNotContain("linear-gradient", html);
    }
}
