using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FbApi.WebhookService.Services;

public class FacebookPayloadParser
{
    public List<ParsedChange> Parse(string rawJson)
    {
        var results = new List<ParsedChange>();

        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("entry", out var entries))
            return results;

        foreach (var entry in entries.EnumerateArray())
        {
            var pageId = GetStringProp(entry, "id");
            var time = GetIntProp(entry, "time");

            if (!entry.TryGetProperty("changes", out var changes))
                continue;

            foreach (var change in changes.EnumerateArray())
            {
                var field = GetStringProp(change, "field");

                if (!change.TryGetProperty("value", out var value))
                    continue;

                var result = new ParsedChange
                {
                    PageId = pageId,
                    Field = field,
                    Timestamp = time > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(time).UtcDateTime
                        : DateTime.UtcNow,
                    PostId = GetStringProp(value, "post_id"),
                    CommentId = GetStringProp(value, "comment_id"),
                    ParentId = GetStringProp(value, "parent_id"),
                    Verb = GetStringProp(value, "verb"),
                    FromId = GetStringProp(value, "from", "id"),
                    FromName = GetStringProp(value, "from", "name"),
                    Message = GetStringProp(value, "message"),
                    Item = GetStringProp(value, "item"),
                    CreatedTime = GetIntProp(value, "created_time"),
                    RawValueJson = value.GetRawText()
                };

                results.Add(result);
            }
        }

        return results;
    }

    private static string GetStringProp(JsonElement element, string prop, string? nested = null)
    {
        if (!element.TryGetProperty(prop, out var value))
            return string.Empty;

        if (nested != null && value.ValueKind == JsonValueKind.Object)
        {
            return value.TryGetProperty(nested, out var nv) ? nv.GetString() ?? string.Empty : string.Empty;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            // For "from" as an object with id and name handled via nested param
            var json = value.GetRawText();
            using var inner = JsonDocument.Parse(json);
            if (inner.RootElement.TryGetProperty("id", out var idElem))
                return idElem.GetString() ?? string.Empty;
            return json;
        }

        return value.GetString() ?? string.Empty;
    }

    private static long GetIntProp(JsonElement element, string prop)
    {
        return element.TryGetProperty(prop, out var value)
            && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;
    }
}

public class ParsedChange
{
    public string PageId { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string PostId { get; set; } = string.Empty;
    public string CommentId { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
    public string Verb { get; set; } = string.Empty;
    public string FromId { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public long CreatedTime { get; set; }
    public string RawValueJson { get; set; } = string.Empty;
}
