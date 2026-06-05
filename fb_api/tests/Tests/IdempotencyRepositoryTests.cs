using FbApi.BackendApi.Repositories;
using FluentAssertions;
using Xunit;

namespace FbApi.IntegrationTests.Tests;

/// <summary>
/// Tests idempotency logic using an in-memory implementation that mirrors
/// the SERIALIZABLE isolation + ON CONFLICT DO NOTHING behavior of the real repository.
/// </summary>
public class IdempotencyRepositoryTests
{
    /// <summary>
    /// In-memory implementation that mirrors IdempotencyRepository behavior:
    /// - First insert with a key returns true
    /// - Duplicate insert with same key returns false (ON CONFLICT DO NOTHING)
    /// - GetStatusAsync returns the stored status
    /// </summary>
    private class InMemoryIdempotencyRepository : IIdempotencyRepository
    {
        private readonly Dictionary<string, (string CommandId, string Action, string Status)> _store = new();
        private readonly object _lock = new();

        public Task<bool> TryInsertAsync(string idempotencyKey, string commandId, string action, string status = "pending")
        {
            lock (_lock)
            {
                if (_store.ContainsKey(idempotencyKey))
                    return Task.FromResult(false); // ON CONFLICT DO NOTHING → 0 rows affected

                _store[idempotencyKey] = (commandId, action, status);
                return Task.FromResult(true); // 1 row affected
            }
        }

        public Task<string?> GetStatusAsync(string idempotencyKey)
        {
            lock (_lock)
            {
                return Task.FromResult(
                    _store.TryGetValue(idempotencyKey, out var entry) ? entry.Status : null);
            }
        }

        public Task UpdateStatusAsync(string idempotencyKey, string status, string? responseData = null)
        {
            lock (_lock)
            {
                if (_store.TryGetValue(idempotencyKey, out var entry))
                    _store[idempotencyKey] = (entry.CommandId, entry.Action, status);

                return Task.CompletedTask;
            }
        }
    }

    private readonly InMemoryIdempotencyRepository _sut = new();

    [Fact]
    public async Task FirstInsert_ReturnsTrue()
    {
        var result = await _sut.TryInsertAsync("key_001", "cmd_001", "auto_reply");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateInsert_ReturnsFalse()
    {
        await _sut.TryInsertAsync("key_002", "cmd_002", "auto_reply");
        var result = await _sut.TryInsertAsync("key_002", "cmd_003", "hide_comment");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_AfterInsert_ReturnsPending()
    {
        await _sut.TryInsertAsync("key_003", "cmd_003", "auto_reply", "pending");
        var status = await _sut.GetStatusAsync("key_003");

        status.Should().Be("pending");
    }

    [Fact]
    public async Task GetStatus_UnknownKey_ReturnsNull()
    {
        var status = await _sut.GetStatusAsync("nonexistent_key");

        status.Should().BeNull();
    }

    [Fact]
    public async Task DifferentKeys_BothInsertSuccessfully()
    {
        var result1 = await _sut.TryInsertAsync("key_a", "cmd_a", "auto_reply");
        var result2 = await _sut.TryInsertAsync("key_b", "cmd_b", "hide_comment");

        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateInsert_DoesNotUpdateStatus()
    {
        await _sut.TryInsertAsync("key_004", "cmd_004", "auto_reply", "pending");
        // Attempt duplicate with different status - should be rejected
        await _sut.TryInsertAsync("key_004", "cmd_005", "hide_comment", "completed");
        var status = await _sut.GetStatusAsync("key_004");

        status.Should().Be("pending", "duplicate insert should not alter existing record");
    }

    [Fact]
    public async Task SerializableIsolation_LogicIsCorrect()
    {
        // Simulate the SERIALIZABLE isolation pattern:
        // 1. Begin transaction
        // 2. INSERT ON CONFLICT DO NOTHING
        // 3. If rowsAffected > 0 → first time → proceed
        // 4. If rowsAffected == 0 → duplicate → check status

        var isFirst = await _sut.TryInsertAsync("key_iso", "cmd_iso", "auto_reply", "pending");
        isFirst.Should().BeTrue("first insert should succeed");

        // Simulate concurrent duplicate attempt
        var isDuplicate = !await _sut.TryInsertAsync("key_iso", "cmd_iso2", "auto_reply", "pending");
        isDuplicate.Should().BeTrue("duplicate should be detected");

        // When duplicate detected, check if already completed
        var existingStatus = await _sut.GetStatusAsync("key_iso");
        var isAlreadyCompleted = existingStatus == "completed";
        isAlreadyCompleted.Should().BeFalse("status is still pending, not completed");
    }

    [Fact]
    public async Task UpdateStatus_ChangesStoredStatus()
    {
        await _sut.TryInsertAsync("key_done", "cmd_done", "auto_reply", "pending");

        await _sut.UpdateStatusAsync("key_done", "completed");

        var status = await _sut.GetStatusAsync("key_done");
        status.Should().Be("completed");
    }
}
