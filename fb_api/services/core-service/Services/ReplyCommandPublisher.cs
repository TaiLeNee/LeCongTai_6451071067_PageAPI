using System.Text.Json;
using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Confluent.Kafka;

namespace FbApi.CoreService.Services;

public interface IReplyCommandPublisher
{
    Task PublishAsync(ReplyCommand command);
}

public class ReplyCommandPublisher : IReplyCommandPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<ReplyCommandPublisher> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ReplyCommandPublisher(IConfiguration configuration, ILogger<ReplyCommandPublisher> logger)
    {
        _logger = logger;
        var bootstrapServers = configuration["Kafka:BootstrapServers"] ?? "kafka:9092";
        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            LingerMs = 10,
            CompressionType = CompressionType.Snappy
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(ReplyCommand command)
    {
        var json = JsonSerializer.Serialize(command, JsonOptions);
        var message = new Message<string, string>
        {
            Key = command.IdempotencyKey,
            Value = json
        };

        try
        {
            var dr = await _producer.ProduceAsync(KafkaTopics.ReplyCommands, message);
            _logger.LogInformation("Published ReplyCommand {CommandId} to topic {Topic} [partition:{Partition} offset:{Offset}]",
                command.CommandId, KafkaTopics.ReplyCommands, dr.Partition, dr.Offset);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Failed to publish ReplyCommand {CommandId}: {Error}", command.CommandId, ex.Error.Reason);
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
