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
    private readonly ILogger<EmailRabbitMqWorker> _logger;
    private readonly IEmailSender _emailSender;
    private readonly RabbitMqSettings _rabbitMqSettings;
    private IConnection? _connection;
    private IChannel? _channel;

    public EmailRabbitMqWorker(ILogger<EmailRabbitMqWorker> logger, IEmailSender emailSender, IOptions<RabbitMqSettings> rabbitMqSettings)
    {
        _logger = logger;
        _emailSender = emailSender;
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

        await _channel.ExchangeDeclareAsync(exchange: _rabbitMqSettings.DlxName, type: ExchangeType.Direct, durable: true);

        await _channel.QueueDeclareAsync(queue: _rabbitMqSettings.DlqName, durable: true, exclusive: false, autoDelete: false, arguments: null);
        await _channel.QueueBindAsync(queue: _rabbitMqSettings.DlqName, exchange: _rabbitMqSettings.DlxName, routingKey: _rabbitMqSettings.DlqName);

        var retryQueueArgs = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", "" },
            { "x-dead-letter-routing-key", _rabbitMqSettings.MainQueue },
            { "x-message-ttl", _rabbitMqSettings.RetryDelayMs },
        };
        await _channel.QueueDeclareAsync(queue: _rabbitMqSettings.RetryQueue, durable: true, exclusive: false, autoDelete: false, arguments: retryQueueArgs!);

        var mainQueueArgs = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", _rabbitMqSettings.DlxName },
            { "x-dead-letter-routing-key", _rabbitMqSettings.DlqName }
        };
        await _channel.QueueDeclareAsync(queue: _rabbitMqSettings.MainQueue, durable: true, exclusive: false, autoDelete: false, arguments: mainQueueArgs!);
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
                    var retryCount = GetRetryCount(ea.BasicProperties.Headers!);

                    try
                    {
                        var body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);
                        var emailMessage = JsonSerializer.Deserialize<MessageDto>(message);

                        if (emailMessage?.Email is null)
                        {
                            _logger.LogWarning("Invalid message: missing email");
                            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                            return;
                        }

                        await _emailSender.SendEmailAsync(
                            emailMessage.Email,
                            emailMessage.Subject ?? "",
                            emailMessage.HtmlBody ?? "",
                            cancellationToken);

                        _logger.LogInformation("Email sent to {Email}", emailMessage.Email);
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
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Transient error during email sending. Retry count: {RetryCount}", retryCount);

                        if (retryCount < _rabbitMqSettings.MaxRetries)
                        {
                            try
                            {
                                var headers = ea.BasicProperties.Headers is not null
                                    ? new Dictionary<string, object>(ea.BasicProperties.Headers!)
                                    : new Dictionary<string, object>();

                                headers["x-retry-count"] = retryCount + 1;

                                var props = new BasicProperties { Headers = headers!};

                                await _channel!.BasicPublishAsync(
                                    exchange: "",
                                    routingKey: _rabbitMqSettings.RetryQueue,
                                    mandatory: true,
                                    basicProperties: props,
                                    body: ea.Body);

                                await _channel.BasicAckAsync(ea.DeliveryTag, multiple: false);
                                _logger.LogInformation("Message requeued for retry {RetryCount}/{MaxRetries}", retryCount + 1, _rabbitMqSettings.MaxRetries);
                            }
                            catch (Exception innerEx)
                            {
                                _logger.LogError(innerEx, "Failed to publish to retry queue. Sending original message to DLQ.");
                                await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                            }
                        }
                        else
                        {
                            _logger.LogError("Message failed after {MaxRetries} retries. Sending to DLQ.", _rabbitMqSettings.MaxRetries);
                            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);
                        }
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

    private static int GetRetryCount(IDictionary<string, object>? headers)
    {
        if (headers == null || !headers.TryGetValue("x-retry-count", out var value))
            return 0;

        return Convert.ToInt32(value);
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