using System.Data;
using Dapper;
using Npgsql;

namespace FbApi.BackendApi.Repositories;

public interface ICommandStatusRepository
{
    Task InsertAsync(string commandId, string eventId, string correlationId, string action, string status = "pending");
    Task<bool> TryInsertAsync(string commandId, string eventId, string correlationId, string action, string status = "pending");
    Task UpdateStatusAsync(string commandId, string status, string? facebookResponse = null, string? errorMessage = null);
    Task<int> IncrementRetryCountAsync(string commandId);
    Task<string?> GetStatusAsync(string commandId);
}

public class CommandStatusRepository : ICommandStatusRepository
{
    private readonly string _connectionString;

    public CommandStatusRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings__DefaultConnection is not configured.");
    }

    public async Task InsertAsync(string commandId, string eventId, string correlationId, string action, string status = "pending")
    {
        await TryInsertAsync(commandId, eventId, correlationId, action, status);
    }

    public async Task<bool> TryInsertAsync(string commandId, string eventId, string correlationId, string action, string status = "pending")
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = @"
            INSERT INTO command_status (command_id, event_id, correlation_id, action, status, created_at, updated_at)
            VALUES (@CommandId, @EventId, @CorrelationId, @Action, @Status, NOW(), NOW())
            ON CONFLICT (command_id) DO NOTHING
            RETURNING 1";

        var inserted = await connection.QuerySingleOrDefaultAsync<int?>(sql, new
        {
            CommandId = commandId,
            EventId = eventId,
            CorrelationId = correlationId,
            Action = action,
            Status = status
        });

        return inserted.HasValue;
    }

    public async Task UpdateStatusAsync(string commandId, string status, string? facebookResponse = null, string? errorMessage = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = @"
            UPDATE command_status
            SET status = @Status,
                facebook_response = COALESCE(@FacebookResponse, facebook_response),
                error_message = COALESCE(@ErrorMessage, error_message),
                updated_at = NOW()
            WHERE command_id = @CommandId";

        await connection.ExecuteAsync(sql, new
        {
            CommandId = commandId,
            Status = status,
            FacebookResponse = facebookResponse,
            ErrorMessage = errorMessage
        });
    }

    public async Task<int> IncrementRetryCountAsync(string commandId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = @"
            UPDATE command_status
            SET retry_count = retry_count + 1,
                updated_at = NOW()
            WHERE command_id = @CommandId
            RETURNING retry_count";

        return await connection.QueryFirstAsync<int>(sql, new { CommandId = commandId });
    }

    public async Task<string?> GetStatusAsync(string commandId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = "SELECT status FROM command_status WHERE command_id = @CommandId";
        return await connection.QueryFirstOrDefaultAsync<string>(sql, new { CommandId = commandId });
    }
}
