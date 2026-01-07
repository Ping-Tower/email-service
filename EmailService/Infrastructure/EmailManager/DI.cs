using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Domain;

namespace Infrastructure.EmailManager;

public static class DI
{
    public static IServiceCollection AddEmailManager(this IServiceCollection services, IConfiguration configuration)
    {
        var sectionName = "SmtpSettings";
        services.Configure<SmtpSettings>(configuration.GetSection(sectionName));
        services.AddTransient<IEmailSender, SmtpEmailSender>();
        services.Decorate<IEmailSender, ResilientEmailSender>();
        return services;
    }
}