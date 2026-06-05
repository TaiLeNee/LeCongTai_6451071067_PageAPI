using Microsoft.Extensions.Logging;

namespace FbApi.CoreService.Services;

public interface IEventStatusTracker
{
    void SetReceived(string eventId);
    void SetProcessing(string eventId);
    void SetCommandPublished(string eventId);
    void SetFailed(string eventId, string error);
    EventStatus? GetStatus(string eventId);
}

public class EventStatus
{
    public string EventId { get; set; } = string.Empty;
    public string Status { get; set; } = "received";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? Error { get; set; }
}

public class EventStatusTracker : IEventStatusTracker
{
    private readonly Dictionary<string, EventStatus> _statuses = new();
    private readonly ILogger<EventStatusTracker> _logger;
    private readonly object _lock = new();

    public EventStatusTracker(ILogger<EventStatusTracker> logger)
    {
        _logger = logger;
    }

    public void SetReceived(string eventId)
    {
        UpdateStatus(eventId, "received");
    }

    public void SetProcessing(string eventId)
    {
        UpdateStatus(eventId, "processing");
    }

    public void SetCommandPublished(string eventId)
    {
        UpdateStatus(eventId, "command_published");
    }

    public void SetFailed(string eventId, string error)
    {
        lock (_lock)
        {
            if (_statuses.TryGetValue(eventId, out var status))
            {
                status.Status = "failed";
                status.Error = error;
                status.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _statuses[eventId] = new EventStatus
                {
                    EventId = eventId,
                    Status = "failed",
                    Error = error,
                    UpdatedAt = DateTime.UtcNow
                };
            }
        }
        _logger.LogError("Event {EventId} failed: {Error}", eventId, error);
    }

    public EventStatus? GetStatus(string eventId)
    {
        lock (_lock)
        {
            return _statuses.TryGetValue(eventId, out var status) ? status : null;
        }
    }

    private void UpdateStatus(string eventId, string newStatus)
    {
        lock (_lock)
        {
            if (_statuses.TryGetValue(eventId, out var status))
            {
                status.Status = newStatus;
                status.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _statuses[eventId] = new EventStatus
                {
                    EventId = eventId,
                    Status = newStatus,
                    UpdatedAt = DateTime.UtcNow
                };
            }
        }
        _logger.LogDebug("Event {EventId} status → {Status}", eventId, newStatus);
    }
}
