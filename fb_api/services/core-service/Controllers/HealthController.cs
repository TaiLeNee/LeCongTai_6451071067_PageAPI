using Confluent.Kafka;
using FbApi.CoreService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FbApi.CoreService.Controllers;

[ApiController]
[Route("[controller]")]
public class HealthController : ControllerBase
{
    private readonly IEventStatusTracker _statusTracker;

    public HealthController(IEventStatusTracker statusTracker)
    {
        _statusTracker = statusTracker;
    }

    [HttpGet("/health")]
    public IActionResult Liveness() => Ok(new { status = "alive", timestamp = DateTime.UtcNow });

    [HttpGet("/health/ready")]
    public IActionResult Readiness()
    {
        try
        {
            var kafkaServer = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Kafka:BootstrapServers"] ?? "kafka:9092";
            var adminConfig = new AdminClientConfig { BootstrapServers = kafkaServer };
            using var admin = new AdminClientBuilder(adminConfig).Build();
            var meta = admin.GetMetadata(TimeSpan.FromSeconds(5));

            if (meta.Brokers.Count > 0)
                return Ok(new { status = "ready", kafka_brokers = meta.Brokers.Count, timestamp = DateTime.UtcNow });

            return StatusCode(503, new { status = "not_ready", reason = "No Kafka brokers found" });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { status = "not_ready", reason = ex.Message });
        }
    }
}
