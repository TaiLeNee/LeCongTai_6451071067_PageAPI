using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using FbApi.BackendApi.Repositories;
using FbApi.BackendApi.Services;
using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;

namespace FbApi.BackendApi.Workers;

public class ReplyCommandWorker : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IIdempotencyRepository _idempotencyRepository;
    private readonly ICommandStatusRepository _commandStatusRepository;
    private readonly IFacebookActionService _facebookActionService;
    private readonly IFacebookApiErrorHandler _errorHandler;
    private readonly ISendFailedPublisher _sendFailedPublisher;
    private readonly ILogger<ReplyCommandWorker> _logger;

    private IConsumer<string, string>? _consumer;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ReplyCommandWorker(
        IConfiguration configuration,
        IIdempotencyRepository idempotencyRepository,
        ICommandStatusRepository commandStatusRepository,
        IFacebookActionService facebookActionService,
        IFacebookApiErrorHandler errorHandler,
        ISendFailedPublisher sendFailedPublisher,
        ILogger<ReplyCommandWorker> logger)
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
            GroupId = "backend-api-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        _consumer = new ConsumerBuilder<string, string>(config).Build();
        _consumer.Subscribe(KafkaTopics.ReplyCommands);

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
                        _logger.LogError(ex, "Kafka consume error in ReplyCommandWorker");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error in ReplyCommandWorker");
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
        var command = JsonSerializer.Deserialize<ReplyCommand>(message.Value, JsonOptions);
        if (command == null)
        {
            _logger.LogWarning("Failed to deserialize ReplyCommand: {Value}", message.Value);
            return;
        }

        var isNewCommand = await _commandStatusRepository.TryInsertAsync(
            command.CommandId, command.EventId, command.CorrelationId, command.Action, "pending");

        if (!isNewCommand)
        {
            var existingStatus = await _commandStatusRepository.GetStatusAsync(command.CommandId);
            _logger.LogInformation(
                "Skipping duplicate command {CommandId}; command already has status {Status}",
                command.CommandId, existingStatus ?? "unknown");
            return;
        }

        var isNewIdempotencyKey = await _idempotencyRepository.TryInsertAsync(
            command.IdempotencyKey, command.CommandId, command.Action, "pending");

        if (!isNewIdempotencyKey)
        {
            var existingStatus = await _idempotencyRepository.GetStatusAsync(command.IdempotencyKey);
            await _commandStatusRepository.UpdateStatusAsync(
                command.CommandId,
                "duplicate",
                errorMessage: $"Idempotency key {command.IdempotencyKey} already has status {existingStatus ?? "unknown"}");

            _logger.LogInformation(
                "Skipping duplicate command {CommandId}; idempotency key {IdempotencyKey} already has status {Status}",
                command.CommandId, command.IdempotencyKey, existingStatus ?? "unknown");
            return;
        }

        await _idempotencyRepository.UpdateStatusAsync(command.IdempotencyKey, "processing");

        await _commandStatusRepository.UpdateStatusAsync(command.CommandId, "processing");

        try
        {
            if (command.Action == ActionTypes.AutoReply)
            {
                var result = await _facebookActionService.SendAutoReplyAsync(
                    command.Target.PageId, command.Target.CommentId, command.ReplyText);

                if (result.Success && command.ShouldHide)
                {
                    var hideResult = await _facebookActionService.HideCommentAsync(
                        command.Target.PageId, command.Target.CommentId);
                    if (hideResult.Success)
                    {
                        await CompleteAsync(command, $"reply:{result.ResponseData};hide:{hideResult.ResponseData}");
                    }
                    else
                    {
                        await HandleFailureAsync(command, hideResult.ErrorMessage ?? "Unknown error");
                    }
                }
                else if (result.Success)
                {
                    await CompleteAsync(command, result.ResponseData);
                }
                else
                {
                    await HandleFailureAsync(command, result.ErrorMessage ?? "Unknown error");
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
                    await HandleFailureAsync(command, result.ErrorMessage ?? "Unknown error");
                }
            }
            else if (command.Action == ActionTypes.ManualReview || command.Action == ActionTypes.BlacklistUser)
            {
                await _commandStatusRepository.UpdateStatusAsync(
                    command.CommandId, command.Action, errorMessage: command.Reason);
                await _idempotencyRepository.UpdateStatusAsync(command.IdempotencyKey, command.Action);
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
            await HandleFailureAsync(command, ex.Message);
        }
    }

    private async Task CompleteAsync(ReplyCommand command, string? facebookResponse)
    {
        await _commandStatusRepository.UpdateStatusAsync(
            command.CommandId, "completed", facebookResponse: facebookResponse);
        await _idempotencyRepository.UpdateStatusAsync(
            command.IdempotencyKey, "completed", facebookResponse);
    }

    private async Task HandleFailureAsync(ReplyCommand command, string errorMessage)
    {
        var classification = _errorHandler.ClassifyError(new Exception(errorMessage));
        var retryCount = await _commandStatusRepository.IncrementRetryCountAsync(command.CommandId);
        var maxRetryCount = _configuration.GetValue<int>("Retry:MaxRetryCount", 5);

        if (classification.Classification == FacebookErrorClassification.Permanent)
        {
            await _commandStatusRepository.UpdateStatusAsync(
                command.CommandId, "failed_permanent", errorMessage: errorMessage);
            await _idempotencyRepository.UpdateStatusAsync(command.IdempotencyKey, "failed_permanent");
            await PublishFailureAsync(command, errorMessage, maxRetryCount, classification);
            return;
        }

        await _commandStatusRepository.UpdateStatusAsync(
            command.CommandId, "failed_retryable", errorMessage: errorMessage);
        await _idempotencyRepository.UpdateStatusAsync(command.IdempotencyKey, "failed_retryable");

        await PublishFailureAsync(command, errorMessage, retryCount, classification);
    }

    private async Task PublishFailureAsync(
        ReplyCommand command,
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
            Payload = JsonSerializer.Serialize(command, JsonOptions),
            FailedAt = DateTime.UtcNow
        });
    }

    public override void Dispose()
    {
        _consumer?.Dispose();
        base.Dispose();
    }
}
