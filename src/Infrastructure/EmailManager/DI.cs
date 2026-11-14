using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Domain;

namespace Infrastructure.EmailManager;

public static class DI
{
    public static IServiceCollection AddEmailManager(this IServiceCollection services, IConfiguration configuration)
    {
        var sectionName = "SmtpSettings";
        services.AddOptions<SmtpSettings>()
            .Bind(configuration.GetSection(sectionName))
            .Validate(settings => settings.TimeoutSeconds > 0 && settings.TimeoutSeconds <= 300,
                "SMTP timeout must be between 1 and 300 seconds.")
            .Validate(settings => new[] { "starttls", "ssl", "tls" }.Contains(settings.Security.ToLowerInvariant()),
                "SMTP security must be starttls, ssl or tls.")
            .ValidateOnStart();
        services.AddSingleton<IEmailTemplateRenderer, FileSystemEmailTemplateRenderer>();
        services.AddTransient<IEmailSender, SmtpEmailSender>();
        services.Decorate<IEmailSender, ResilientEmailSender>();
        return services;
    }
} 
