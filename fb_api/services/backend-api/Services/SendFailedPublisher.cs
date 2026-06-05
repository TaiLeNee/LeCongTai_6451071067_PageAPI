using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;

namespace FbApi.BackendApi.Services;

public interface ISendFailedPublisher
{
    Task PublishAsync(SendFailedMessage message);
}

public class SendFailedPublisher : ISendFailedPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SendFailedPublisher(IConfiguration configuration)
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

    public async Task PublishAsync(SendFailedMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _producer.ProduceAsync(KafkaTopics.SendFailed, new Message<string, string>
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
