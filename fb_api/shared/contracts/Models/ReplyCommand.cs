using System;

namespace FbApi.Contracts.Models;

public class ReplyCommand
{
    public string SchemaVersion { get; set; } = "1.0";
    public string CommandId { get; set; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public ReplyTarget Target { get; set; } = new();
    public string ReplyText { get; set; } = string.Empty;
    public bool ShouldHide { get; set; }
    public bool RequiresManualReview { get; set; }
    public bool InternalBlacklist { get; set; }
    public string Intent { get; set; } = string.Empty;
    public string Sentiment { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReplyTarget
{
    public string PageId { get; set; } = string.Empty;
    public string PostId { get; set; } = string.Empty;
    public string CommentId { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
}
