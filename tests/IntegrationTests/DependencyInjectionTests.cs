using Domain;
using Infrastructure.EmailManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddEmailManager_RegistersTemplateRendererAndDecoratedEmailSender()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SmtpSettings:Host"] = "smtp.example.com",
                ["SmtpSettings:Port"] = "587",
                ["SmtpSettings:UserName"] = "user",
                ["SmtpSettings:Password"] = "password",
                ["SmtpSettings:FromAddress"] = "noreply@example.com",
                ["SmtpSettings:FromName"] = "PingTower"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEmailManager(configuration);

        using var serviceProvider = services.BuildServiceProvider();

        var renderer = serviceProvider.GetRequiredService<IEmailTemplateRenderer>();
        var sender = serviceProvider.GetRequiredService<IEmailSender>();

        Assert.IsType<FileSystemEmailTemplateRenderer>(renderer);
        Assert.IsType<ResilientEmailSender>(sender);
    }
}
