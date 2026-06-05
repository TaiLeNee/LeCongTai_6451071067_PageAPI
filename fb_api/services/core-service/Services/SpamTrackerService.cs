namespace FbApi.CoreService.Services;

public interface ISpamTrackerService
{
    void RecordSpam(string userId);
    int GetSpamCount(string userId);
    bool IsRepeatedSpammer(string userId);
    void BlacklistUser(string userId);
    bool IsBlacklisted(string userId);
}

public class SpamTrackerService : ISpamTrackerService, IHostedService, IDisposable
{
    private readonly TimeSpan _ttl = TimeSpan.FromHours(24);
    private readonly int _spammerThreshold = 3;
    private readonly Dictionary<string, List<DateTime>> _spamRecords = new();
    private readonly HashSet<string> _blacklistedUsers = new();
    private readonly object _lock = new();
    private Timer? _cleanupTimer;

    public void RecordSpam(string userId)
    {
        lock (_lock)
        {
            if (!_spamRecords.TryGetValue(userId, out var list))
            {
                list = new List<DateTime>();
                _spamRecords[userId] = list;
            }
            list.Add(DateTime.UtcNow);
        }
    }

    public int GetSpamCount(string userId)
    {
        lock (_lock)
        {
            if (!_spamRecords.TryGetValue(userId, out var list))
                return 0;

            var cutoff = DateTime.UtcNow - _ttl;
            return list.Count(dt => dt > cutoff);
        }
    }

    public bool IsRepeatedSpammer(string userId)
    {
        return GetSpamCount(userId) >= _spammerThreshold;
    }

    public void BlacklistUser(string userId)
    {
        lock (_lock)
        {
            _blacklistedUsers.Add(userId);
        }
    }

    public bool IsBlacklisted(string userId)
    {
        lock (_lock)
        {
            return _blacklistedUsers.Contains(userId);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cleanupTimer = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cleanupTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private void Cleanup()
    {
        lock (_lock)
        {
            var cutoff = DateTime.UtcNow - _ttl;
            var emptyUsers = new List<string>();

            foreach (var (userId, list) in _spamRecords)
            {
                list.RemoveAll(dt => dt <= cutoff);
                if (list.Count == 0)
                    emptyUsers.Add(userId);
            }

            foreach (var userId in emptyUsers)
                _spamRecords.Remove(userId);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}
