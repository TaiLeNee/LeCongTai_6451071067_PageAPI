using System.Text.Json;
using Confluent.Kafka;
using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;
using FbApi.CoreService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FbApi.CoreService.Workers;

public class CoreEventWorker : BackgroundService
{
    private readonly IConsumer<string, string> _consumer;
    private readonly ISpamDetectorService _spamDetector;
    private readonly ISpamTrackerService _spamTracker;
    private readonly IGeminiService _geminiService;
    private readonly IRuleEngineService _ruleEngine;
    private readonly IReplyCommandPublisher _publisher;
    private readonly IEventStatusTracker _statusTracker;
    private readonly ILogger<CoreEventWorker> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CoreEventWorker(
        IConfiguration configuration,
        ISpamDetectorService spamDetector,
        ISpamTrackerService spamTracker,
        IGeminiService geminiService,
        IRuleEngineService ruleEngine,
        IReplyCommandPublisher publisher,
        IEventStatusTracker statusTracker,
        ILogger<CoreEventWorker> logger)
    {
        _spamDetector = spamDetector;
        _spamTracker = spamTracker;
        _geminiService = geminiService;
        _ruleEngine = ruleEngine;
        _publisher = publisher;
        _statusTracker = statusTracker;
        _logger = logger;

        var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "kafka:9092";
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = "core-service-group",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            MaxPollIntervalMs = 300000
        };
        _consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        _consumer.Subscribe(KafkaTopics.RawEvents);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CoreEventWorker started, consuming from {Topic}", KafkaTopics.RawEvents);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var consumeResult = _consumer.Consume(stoppingToken);
                if (consumeResult == null) continue;

                await ProcessEventAsync(consumeResult, stoppingToken);
                _consumer.Commit(consumeResult);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Kafka consume error: {Error}", ex.Error.Reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in CoreEventWorker");
            }
        }

        _logger.LogInformation("CoreEventWorker stopping");
    }

    private async Task ProcessEventAsync(ConsumeResult<string, string> consumeResult, CancellationToken ct)
    {
        NormalizedEvent? evt = null;
        try
        {
            evt = JsonSerializer.Deserialize<NormalizedEvent>(consumeResult.Message.Value, JsonOptions);
            if (evt == null)
            {
                _logger.LogWarning("Failed to deserialize NormalizedEvent from message at partition:{Partition} offset:{Offset}",
                    consumeResult.Partition, consumeResult.Offset);
                return;
            }

            if (string.IsNullOrWhiteSpace(evt.CommentId) ||
                string.IsNullOrWhiteSpace(evt.Message) ||
                string.Equals(evt.UserId, evt.PageId, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Skipping event {EventId}: not an external user comment",
                    evt.EventId);
                return;
            }

            _statusTracker.SetReceived(evt.EventId);
            _statusTracker.SetProcessing(evt.EventId);
            _logger.LogInformation("Processing event {EventId} from user {UserId}", evt.EventId, evt.UserId);

            var spamResult = _spamDetector.Detect(evt);

            if (spamResult.IsSpam)
            {
                _spamTracker.RecordSpam(evt.UserId);
                _logger.LogInformation("Spam detected: type={SpamType} for user {UserId}", spamResult.SpamType, evt.UserId);
            }

            AiAnalysisResult aiResult;
            if (spamResult.IsSpam && spamResult.SpamType is "scam" or "link")
            {
                aiResult = new AiAnalysisResult
                {
                    Intent = "spam",
                    Sentiment = "neutral",
                    Confidence = 0.9,
                    AnalyzedAt = DateTime.UtcNow
                };
            }
            else
            {
                aiResult = await _geminiService.AnalyzeAsync(evt);
            }

            var replyCommand = _ruleEngine.Evaluate(evt, spamResult, aiResult);
            await _publisher.PublishAsync(replyCommand);
            _statusTracker.SetCommandPublished(evt.EventId);

            _logger.LogInformation("Event {EventId} → action={Action} hide={Hide} review={Review}",
                evt.EventId, replyCommand.Action, replyCommand.ShouldHide, replyCommand.RequiresManualReview);
        }
        catch (Exception ex)
        {
            var eventId = evt?.EventId ?? "unknown";
            _statusTracker.SetFailed(eventId, ex.Message);
            _logger.LogError(ex, "Failed to process event {EventId}", eventId);
        }
    }

    public override void Dispose()
    {
        _consumer?.Close();
        _consumer?.Dispose();
        base.Dispose();
    }
}
