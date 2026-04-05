using System.Net;
using System.Text.RegularExpressions;
using Domain;

namespace Infrastructure.EmailManager;

public class FileSystemEmailTemplateRenderer : IEmailTemplateRenderer
{
    private static readonly Regex PlaceholderPattern = new(@"\{\{\s*([a-zA-Z0-9_.-]+)\s*\}\}", RegexOptions.Compiled);

    public async Task<(string Subject, string HtmlBody)> RenderAsync(
        string templateId,
        IReadOnlyDictionary<string, string?> data,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            throw new InvalidOperationException("Email template id is required.");

        var templateDirectory = Path.Combine(AppContext.BaseDirectory, "Templates", templateId);
        var subjectPath = Path.Combine(templateDirectory, "subject.txt");
        var bodyPath = Path.Combine(templateDirectory, "body.html");

        if (!File.Exists(subjectPath) || !File.Exists(bodyPath))
            throw new FileNotFoundException($"Email template '{templateId}' was not found.");

        var subjectTemplate = await File.ReadAllTextAsync(subjectPath, cancellationToken);
        var bodyTemplate = await File.ReadAllTextAsync(bodyPath, cancellationToken);

        return (
            Render(subjectTemplate, data, encodeHtml: false),
            Render(bodyTemplate, data, encodeHtml: true));
    }

    private static string Render(string template, IReadOnlyDictionary<string, string?> data, bool encodeHtml)
    {
        return PlaceholderPattern.Replace(
            template,
            match =>
            {
                var key = match.Groups[1].Value;
                data.TryGetValue(key, out var value);
                value ??= string.Empty;
                return encodeHtml ? WebUtility.HtmlEncode(value) : value;
            });
    }
}
