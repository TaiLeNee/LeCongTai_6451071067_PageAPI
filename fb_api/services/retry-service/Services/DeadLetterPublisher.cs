using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;

namespace FbApi.RetryService.Services;

public interface IDeadLetterPublisher
{
    Task PublishAsync(DeadLetterMessage message);
}

public class DeadLetterPublisher : IDeadLetterPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DeadLetterPublisher(IConfiguration configuration)
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

    public async Task PublishAsync(DeadLetterMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _producer.ProduceAsync(KafkaTopics.DeadLetter, new Message<string, string>
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
