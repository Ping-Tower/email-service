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
}
