using System;

namespace FbApi.Contracts.Models;

public class DeadLetterMessage
{
    public string SchemaVersion { get; set; } = "1.0";
    public string CommandId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public string FinalError { get; set; } = string.Empty;
    public string OriginalTopic { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;
}
