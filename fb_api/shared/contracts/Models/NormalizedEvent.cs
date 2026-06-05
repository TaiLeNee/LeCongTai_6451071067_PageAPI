using System;

namespace FbApi.Contracts.Models;

public class NormalizedEvent
{
    public string SchemaVersion { get; set; } = "1.0";
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public string EventType { get; set; } = string.Empty;
    public string Source { get; set; } = "facebook";
    public string PageId { get; set; } = string.Empty;
    public string PostId { get; set; } = string.Empty;
    public string CommentId { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
