using Infrastructure.EmailManager;
using Domain;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Application.DTOs;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Exceptions;
using System.Text.Json;

namespace EmailWorker;

public class EmailRabbitMqWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<EmailRabbitMqWorker> _logger;
    private readonly IEmailSender _emailSender;
    private readonly IEmailTemplateRenderer _emailTemplateRenderer;
    private readonly RabbitMqSettings _rabbitMqSettings;
    private IConnection? _connection;
    private IChannel? _channel;

    public EmailRabbitMqWorker(
        ILogger<EmailRabbitMqWorker> logger,
        IEmailSender emailSender,
        IEmailTemplateRenderer emailTemplateRenderer,
        IOptions<RabbitMqSettings> rabbitMqSettings)
    {
        _logger = logger;
        _emailSender = emailSender;
        _emailTemplateRenderer = emailTemplateRenderer;
        _rabbitMqSettings = rabbitMqSettings.Value;
    }

    private async Task ConnectToRabbitMq(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory()
        {
            HostName = _rabbitMqSettings.HostName!,
            Port = _rabbitMqSettings.Port,
            UserName = _rabbitMqSettings.UserName!,
            Password = _rabbitMqSettings.Password!
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync();
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_channel is null || _channel.IsClosed)
                {
                    await ConnectToRabbitMq(cancellationToken);
                }

                var consumer = new AsyncEventingBasicConsumer(_channel!);
                consumer.ReceivedAsync += async (model, ea) =>
                {
                    try
                    {
                        var body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);
                        var emailMessage = JsonSerializer.Deserialize<MessageDto>(message, JsonSerializerOptions);

                        if (emailMessage?.Email is null || string.IsNullOrWhiteSpace(emailMessage.TemplateId))
                        {
                            _logger.LogWarning("Invalid message: missing email or template id");
                            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                            return;
                        }

                        var (subject, htmlBody) = await _emailTemplateRenderer.RenderAsync(
                            emailMessage.TemplateId,
                            emailMessage.Data,
                            cancellationToken);

                        await _emailSender.SendEmailAsync(
                            emailMessage.Email,
                            subject,
                            htmlBody,
                            cancellationToken);

                        _logger.LogInformation("Email sent to {Email} using template {TemplateId}", emailMessage.Email, emailMessage.TemplateId);
                        await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "Invalid JSON format. Sending message directly to DLQ.");
                        await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                    catch (ArgumentNullException argNullEx) when (argNullEx.ParamName == nameof(MessageDto.Email))
                    {
                        _logger.LogWarning("Message rejected due to missing email. Sending to DLQ.");
                        await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                    catch (FileNotFoundException templateEx)
                    {
                        _logger.LogError(templateEx, "Email template not found. Sending message to DLQ.");
                        await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during email sending. Message will be rejected.");
                        await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                    }
                };

                await _channel!.BasicConsumeAsync(
                    queue: _rabbitMqSettings.MainQueue,
                    autoAck: false,
                    consumer: consumer);

                while (!_channel!.IsClosed && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.Initiator == ShutdownInitiator.Peer)
            {
                _logger.LogWarning("RabbitMQ connection lost. Reconnecting in 5 seconds. Reason: {Reason}", ex.ShutdownReason?.ToString());
                await Task.Delay(5000, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in RabbitMQ consumer. Reconnecting in 5 seconds...");
                await Task.Delay(5000, cancellationToken);
            }
            finally
            {
                if (_channel is not null && !_channel.IsClosed)
                {
                    await _channel.CloseAsync();
                    _channel.Dispose();
                    _channel = null;
                }
                if (_connection is not null && _connection.IsOpen)
                {
                    await _connection.CloseAsync();
                    _connection.Dispose();
                    _connection = null;
                }
            }
        }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            _channel.Dispose();
        }

        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        await base.StopAsync(cancellationToken);
    }
}
