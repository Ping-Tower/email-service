using Infrastructure;
using EmailWorker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);

var sectionName = "RabbitMqSettings";

builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection(sectionName));

builder.Services.AddHostedService<EmailRabbitMqWorker>();

var host = builder.Build();
host.Run();