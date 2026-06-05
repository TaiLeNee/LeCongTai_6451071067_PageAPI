using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;
using FbApi.RetryService.Services;

namespace FbApi.RetryService.Workers;

public class RetryWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IRetryPolicyService _retryPolicyService;
    private readonly ISendRetryPublisher _sendRetryPublisher;
    private readonly IDeadLetterPublisher _deadLetterPublisher;
    private readonly ILogger<RetryWorker> _logger;

    private IConsumer<string, string>? _consumer;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public RetryWorker(
        IConfiguration configuration,
        IRetryPolicyService retryPolicyService,
        ISendRetryPublisher sendRetryPublisher,
        IDeadLetterPublisher deadLetterPublisher,
        ILogger<RetryWorker> logger)
    {
        _configuration = configuration;
        _retryPolicyService = retryPolicyService;
        _sendRetryPublisher = sendRetryPublisher;
        _deadLetterPublisher = deadLetterPublisher;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka__BootstrapServers is not configured.");

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "retry-service-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _consumer.Subscribe(KafkaTopics.SendFailed);

        return Task.Run(async () =>
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var consumeResult = _consumer.Consume(stoppingToken);
                        if (consumeResult == null) continue;

                        await ProcessMessageAsync(consumeResult.Message, stoppingToken);
                        _consumer.Commit(consumeResult);
                    }
                    catch (OperationCanceledException) { }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Kafka consume error in RetryWorker");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error in RetryWorker");
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                }
            }
            finally
            {
                _consumer.Close();
            }
        }, stoppingToken);
    }

    private async Task ProcessMessageAsync(Message<string, string> message, CancellationToken ct)
    {
        var failedMessage = JsonSerializer.Deserialize<SendFailedMessage>(message.Value, JsonOptions);
        if (failedMessage == null)
        {
            _logger.LogWarning("Failed to deserialize SendFailedMessage: {Value}", message.Value);
            return;
        }

        var maxRetryCount = _configuration.GetValue<int>("Retry:MaxRetryCount", 3);
        var baseDelaySeconds = _configuration.GetValue<int>("Retry:BaseDelaySeconds", 1);
        var baseDelayMs = baseDelaySeconds * 1000;

        if (failedMessage.RetryCount < maxRetryCount)
        {
            var delayMs = _retryPolicyService.GetDelayMs(failedMessage.RetryCount, baseDelayMs);
            _logger.LogInformation(
                "Retrying command {CommandId}, attempt {RetryCount}/{MaxRetry}, delay {DelayMs}ms",
                failedMessage.CommandId, failedMessage.RetryCount + 1, maxRetryCount, delayMs);

            await Task.Delay((int)delayMs, ct);

            var nextRetryAt = DateTime.UtcNow.AddMilliseconds(delayMs);

            var retryMessage = new SendRetryMessage
            {
                CommandId = failedMessage.CommandId,
                CorrelationId = failedMessage.CorrelationId,
                EventId = failedMessage.EventId,
                RetryCount = failedMessage.RetryCount + 1,
                LastError = failedMessage.LastError,
                ErrorType = failedMessage.ErrorType,
                NextRetryAt = nextRetryAt,
                Payload = failedMessage.Payload,
                FailedAt = failedMessage.FailedAt
            };

            await _sendRetryPublisher.PublishAsync(retryMessage);
        }
        else
        {
            _logger.LogWarning(
                "Command {CommandId} exceeded max retries ({RetryCount}), sending to dead letter",
                failedMessage.CommandId, failedMessage.RetryCount);

            var deadLetterMessage = new DeadLetterMessage
            {
                CommandId = failedMessage.CommandId,
                CorrelationId = failedMessage.CorrelationId,
                EventId = failedMessage.EventId,
                RetryCount = failedMessage.RetryCount,
                FinalError = failedMessage.LastError,
                OriginalTopic = KafkaTopics.SendFailed,
                Payload = failedMessage.Payload,
                FailedAt = failedMessage.FailedAt
            };

            await _deadLetterPublisher.PublishAsync(deadLetterMessage);
        }
    }
}
