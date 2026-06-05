using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using FbApi.BackendApi.Repositories;
using FbApi.BackendApi.Services;
using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;

namespace FbApi.BackendApi.Workers;

public class SendRetryWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IIdempotencyRepository _idempotencyRepository;
    private readonly ICommandStatusRepository _commandStatusRepository;
    private readonly IFacebookActionService _facebookActionService;
    private readonly IFacebookApiErrorHandler _errorHandler;
    private readonly ISendFailedPublisher _sendFailedPublisher;
    private readonly ILogger<SendRetryWorker> _logger;

    private IConsumer<string, string>? _consumer;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SendRetryWorker(
        IConfiguration configuration,
        IIdempotencyRepository idempotencyRepository,
        ICommandStatusRepository commandStatusRepository,
        IFacebookActionService facebookActionService,
        IFacebookApiErrorHandler errorHandler,
        ISendFailedPublisher sendFailedPublisher,
        ILogger<SendRetryWorker> logger)
    {
        _configuration = configuration;
        _idempotencyRepository = idempotencyRepository;
        _commandStatusRepository = commandStatusRepository;
        _facebookActionService = facebookActionService;
        _errorHandler = errorHandler;
        _sendFailedPublisher = sendFailedPublisher;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrapServers = _configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka__BootstrapServers is not configured.");

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "backend-api-retry-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _consumer.Subscribe(KafkaTopics.SendRetry);

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
                        _logger.LogError(ex, "Kafka consume error in SendRetryWorker");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error in SendRetryWorker");
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
        var retryMessage = JsonSerializer.Deserialize<SendRetryMessage>(message.Value, JsonOptions);
        if (retryMessage == null)
        {
            _logger.LogWarning("Failed to deserialize SendRetryMessage: {Value}", message.Value);
            return;
        }

        var command = JsonSerializer.Deserialize<ReplyCommand>(retryMessage.Payload, JsonOptions);
        if (command == null)
        {
            _logger.LogWarning("Failed to deserialize ReplyCommand from retry payload: {Payload}", retryMessage.Payload);
            return;
        }

        var existingStatus = await _idempotencyRepository.GetStatusAsync(command.IdempotencyKey);
        if (existingStatus == "completed")
        {
            _logger.LogInformation(
                "Skipping retry for command {CommandId}; idempotency key {IdempotencyKey} is already completed",
                command.CommandId, command.IdempotencyKey);
            return;
        }

        if (existingStatus == "processing_retry")
        {
            _logger.LogInformation(
                "Skipping duplicate in-flight retry for command {CommandId}; attempt {RetryCount}",
                command.CommandId, retryMessage.RetryCount);
            return;
        }

        if (existingStatus == null)
        {
            await _idempotencyRepository.TryInsertAsync(
                command.IdempotencyKey, command.CommandId, command.Action, "processing_retry");
        }
        else
        {
            await _idempotencyRepository.UpdateStatusAsync(command.IdempotencyKey, "processing_retry");
        }

        await _commandStatusRepository.UpdateStatusAsync(command.CommandId, "processing");

        try
        {
            if (command.Action == ActionTypes.AutoReply)
            {
                var result = await _facebookActionService.SendAutoReplyAsync(
                    command.Target.PageId, command.Target.CommentId, command.ReplyText);

                if (result.Success)
                {
                    await CompleteAsync(command, result.ResponseData);
                }
                else
                {
                    await HandleFailureAsync(command, retryMessage, result.ErrorMessage ?? "Unknown error");
                }
            }
            else if (command.Action == ActionTypes.HideComment)
            {
                var result = await _facebookActionService.HideCommentAsync(
                    command.Target.PageId, command.Target.CommentId);

                if (result.Success)
                {
                    await CompleteAsync(command, result.ResponseData);
                }
                else
                {
                    await HandleFailureAsync(command, retryMessage, result.ErrorMessage ?? "Unknown error");
                }
            }
            else
            {
                await _commandStatusRepository.UpdateStatusAsync(
                    command.CommandId, "failed_permanent", errorMessage: $"Unsupported action: {command.Action}");
                await _idempotencyRepository.UpdateStatusAsync(command.IdempotencyKey, "failed_permanent");
            }
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(command, retryMessage, ex.Message);
        }
    }

    private async Task CompleteAsync(ReplyCommand command, string? facebookResponse)
    {
        await _commandStatusRepository.UpdateStatusAsync(
            command.CommandId, "completed", facebookResponse: facebookResponse);
        await _idempotencyRepository.UpdateStatusAsync(
            command.IdempotencyKey, "completed", facebookResponse);
    }

    private async Task HandleFailureAsync(ReplyCommand command, SendRetryMessage retryMessage, string errorMessage)
    {
        var classification = _errorHandler.ClassifyError(new Exception(errorMessage));
        var retryCount = await _commandStatusRepository.IncrementRetryCountAsync(command.CommandId);
        var maxRetryCount = _configuration.GetValue<int>("Retry:MaxRetryCount", 5);

        if (classification.Classification == FacebookErrorClassification.Permanent || retryCount >= maxRetryCount)
        {
            await _commandStatusRepository.UpdateStatusAsync(
                command.CommandId, "failed_permanent", errorMessage: errorMessage);
            await _idempotencyRepository.UpdateStatusAsync(command.IdempotencyKey, "failed_permanent");
            await PublishFailureAsync(command, retryMessage, errorMessage, Math.Max(retryCount, maxRetryCount), classification);
            return;
        }

        await _commandStatusRepository.UpdateStatusAsync(
            command.CommandId, "failed_retryable", errorMessage: errorMessage);
        await _idempotencyRepository.UpdateStatusAsync(command.IdempotencyKey, "failed_retryable");

        await PublishFailureAsync(command, retryMessage, errorMessage, retryCount, classification);
    }

    private async Task PublishFailureAsync(
        ReplyCommand command,
        SendRetryMessage retryMessage,
        string errorMessage,
        int retryCount,
        ClassifiedError classification)
    {
        await _sendFailedPublisher.PublishAsync(new SendFailedMessage
        {
            CommandId = command.CommandId,
            CorrelationId = command.CorrelationId,
            EventId = command.EventId,
            RetryCount = retryCount,
            LastError = errorMessage,
            ErrorType = classification.Classification.ToString(),
            NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Min(Math.Pow(2, retryCount), 60)),
            Payload = retryMessage.Payload,
            FailedAt = DateTime.UtcNow
        });
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}
