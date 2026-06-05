using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;

namespace FbApi.RetryService.Services;

public interface ISendRetryPublisher
{
    Task PublishAsync(SendRetryMessage message);
}

public class SendRetryPublisher : ISendRetryPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SendRetryPublisher(IConfiguration configuration)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka__BootstrapServers is not configured.");

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            CompressionType = CompressionType.Snappy
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(SendRetryMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _producer.ProduceAsync(KafkaTopics.SendRetry, new Message<string, string>
        {
            Key = message.CommandId,
            Value = json
        });
    }

    public void Dispose()
    {
        _producer.Dispose();
    }
}
