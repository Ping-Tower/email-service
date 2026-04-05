namespace Domain;

public interface IEmailTemplateRenderer
{
    Task<(string Subject, string HtmlBody)> RenderAsync(
        string templateId,
        IReadOnlyDictionary<string, string?> data,
        CancellationToken cancellationToken);
}
