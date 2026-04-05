using Infrastructure.EmailManager;
using Xunit;

namespace UnitTests;

public class FileSystemEmailTemplateRendererTests
{
    [Fact]
    public async Task RenderAsync_WhenTemplateExists_ReplacesPlaceholdersAndEncodesHtmlBody()
    {
        var templateId = $"unit-template-{Guid.NewGuid():N}";
        var templateDirectory = Path.Combine(AppContext.BaseDirectory, "Templates", templateId);
        Directory.CreateDirectory(templateDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(templateDirectory, "subject.txt"),
            "Hello {{ userName }}");
        await File.WriteAllTextAsync(
            Path.Combine(templateDirectory, "body.html"),
            "<p>User: {{ userName }}</p><p>Link: {{ link }}</p>");

        var renderer = new FileSystemEmailTemplateRenderer();
        var data = new Dictionary<string, string?>
        {
            ["userName"] = "<Admin>",
            ["link"] = "https://example.com?a=1&b=2"
        };

        var (subject, htmlBody) = await renderer.RenderAsync(templateId, data, CancellationToken.None);

        Assert.Equal("Hello <Admin>", subject);
        Assert.Contains("&lt;Admin&gt;", htmlBody);
        Assert.Contains("https://example.com?a=1&amp;b=2", htmlBody);
    }

    [Fact]
    public async Task RenderAsync_WhenTemplateDoesNotExist_ThrowsFileNotFoundException()
    {
        var renderer = new FileSystemEmailTemplateRenderer();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            renderer.RenderAsync(
                $"missing-{Guid.NewGuid():N}",
                new Dictionary<string, string?>(),
                CancellationToken.None));
    }
}
