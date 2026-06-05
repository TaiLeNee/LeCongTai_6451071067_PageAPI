using System;
using System.Security.Cryptography;
using System.Text;
using FbApi.Contracts.Models;

namespace FbApi.WebhookService.Services;

public class NormalizedEventMapper
{
    public NormalizedEvent Map(ParsedChange change, string rawPayload)
    {
        return new NormalizedEvent
        {
            EventId = CreateStableEventId(change),
            CorrelationId = Guid.NewGuid().ToString(),
            EventType = DetermineEventType(change),
            Source = "facebook",
            PageId = change.PageId,
            PostId = change.PostId,
            CommentId = change.CommentId,
            ParentId = change.ParentId,
            UserId = change.FromId,
            UserName = change.FromName,
            Message = change.Message,
            RawPayload = rawPayload,
            CreatedAt = change.CreatedTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(change.CreatedTime).UtcDateTime
                : change.Timestamp
        };
    }

    private static string DetermineEventType(ParsedChange change)
    {
        return change.Field switch
        {
            "feed" => string.IsNullOrEmpty(change.CommentId) ? "post" : "comment",
            "mention" => "mention",
            "message" => "message",
            "conversations" => "conversation",
            _ => change.Field
        };
    }

    private static string CreateStableEventId(ParsedChange change)
    {
        var source = string.Join(':',
            "fb",
            change.PageId,
            change.PostId,
            change.CommentId,
            change.ParentId,
            change.FromId,
            change.Field,
            change.Verb,
            change.CreatedTime,
            change.Message);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"evt_{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }
}
