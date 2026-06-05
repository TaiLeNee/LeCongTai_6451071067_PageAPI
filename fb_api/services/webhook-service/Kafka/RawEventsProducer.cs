using System;
using System.Text.Json;
using System.Threading.Tasks;
using Confluent.Kafka;
using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace FbApi.WebhookService.Kafka;

public class RawEventsProducer : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<RawEventsProducer> _logger;

    public RawEventsProducer(string bootstrapServers, ILogger<RawEventsProducer> logger)
    {
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            CompressionType = CompressionType.Snappy,
            LingerMs = 5,
            MessageSendMaxRetries = 3,
            RetryBackoffMs = 100
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task ProduceAsync(NormalizedEvent evt)
    {
        try
        {
            var json = JsonSerializer.Serialize(evt);

            var message = new Message<string, string>
            {
                Key = evt.PageId,
                Value = json,
                Timestamp = new Timestamp(DateTime.UtcNow)
            };

            var result = await _producer.ProduceAsync(KafkaTopics.RawEvents, message);

            _logger.LogInformation(
                "Produced event {EventId} to {Topic} [{Partition}] @ offset {Offset}",
                evt.EventId, result.Topic, result.Partition, result.Offset);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex,
                "Failed to produce event {EventId}: {Error}",
                evt.EventId, ex.Error.Reason);
            throw;
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
