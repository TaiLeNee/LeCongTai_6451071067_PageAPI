using Confluent.Kafka;
using Microsoft.AspNetCore.Mvc;

namespace FbApi.RetryService.Controllers;

[ApiController]
public class HealthController : ControllerBase
{
    [HttpGet("/health")]
    public IActionResult Liveness() => Ok(new { status = "healthy" });

    [HttpGet("/health/ready")]
    public IActionResult Readiness([FromServices] IConfiguration configuration)
    {
        var bootstrapServers = configuration["Kafka:BootstrapServers"];

        try
        {
            var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
            using var adminClient = new AdminClientBuilder(config).Build();
            var metadata = adminClient.GetMetadata(TimeSpan.FromSeconds(5));
            return Ok(new { status = "ready", checks = new { kafka = "healthy" } });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { status = "unhealthy", checks = new { kafka = $"unhealthy: {ex.Message}" } });
        }
    }
}
