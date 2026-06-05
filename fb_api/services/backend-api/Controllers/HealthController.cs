using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace FbApi.BackendApi.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Liveness() => Ok(new { status = "healthy" });

    [HttpGet("/health/ready")]
    public async Task<IActionResult> Readiness([FromServices] IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync();
            return Ok(new { status = "ready", checks = new { postgresql = "healthy" } });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { status = "unhealthy", checks = new { postgresql = $"unhealthy: {ex.Message}" } });
        }
    }
}
