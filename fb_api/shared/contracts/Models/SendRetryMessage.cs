using System;

namespace FbApi.Contracts.Models;

public class SendRetryMessage
{
    public string SchemaVersion { get; set; } = "1.0";
    public string CommandId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public DateTime? NextRetryAt { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;
}
