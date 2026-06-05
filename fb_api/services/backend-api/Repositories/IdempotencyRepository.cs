using System.Data;
using Dapper;
using Npgsql;

namespace FbApi.BackendApi.Repositories;

public interface IIdempotencyRepository
{
    /// <returns>true if inserted (first time), false if key already exists (duplicate)</returns>
    Task<bool> TryInsertAsync(string idempotencyKey, string commandId, string action, string status = "pending");
    Task<string?> GetStatusAsync(string idempotencyKey);
    Task UpdateStatusAsync(string idempotencyKey, string status, string? responseData = null);
}

public class IdempotencyRepository : IIdempotencyRepository
{
    private readonly string _connectionString;

    public IdempotencyRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings__DefaultConnection is not configured.");
    }

    public async Task<bool> TryInsertAsync(string idempotencyKey, string commandId, string action, string status = "pending")
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.Serializable);

        const string sql = @"
            INSERT INTO idempotency_keys (idempotency_key, command_id, action, status, created_at, updated_at)
            VALUES (@IdempotencyKey, @CommandId, @Action, @Status, NOW(), NOW())
            ON CONFLICT (idempotency_key) DO NOTHING";

        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            IdempotencyKey = idempotencyKey,
            CommandId = commandId,
            Action = action,
            Status = status
        }, tx);

        await tx.CommitAsync();

        return rowsAffected > 0;
    }

    public async Task<string?> GetStatusAsync(string idempotencyKey)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = "SELECT status FROM idempotency_keys WHERE idempotency_key = @IdempotencyKey";
        return await connection.QueryFirstOrDefaultAsync<string>(sql, new { IdempotencyKey = idempotencyKey });
    }

    public async Task UpdateStatusAsync(string idempotencyKey, string status, string? responseData = null)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        const string sql = @"
            UPDATE idempotency_keys
            SET status = @Status,
                response_data = COALESCE(@ResponseData, response_data),
                updated_at = NOW()
            WHERE idempotency_key = @IdempotencyKey";

        await connection.ExecuteAsync(sql, new
        {
            IdempotencyKey = idempotencyKey,
            Status = status,
            ResponseData = responseData
        });
    }
}
