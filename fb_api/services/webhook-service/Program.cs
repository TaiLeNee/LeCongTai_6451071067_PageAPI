using FbApi.WebhookService.Controllers;
using FbApi.WebhookService.Kafka;
using FbApi.WebhookService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

// Controllers
builder.Services.AddControllers()
    .AddApplicationPart(typeof(WebhookController).Assembly);

// Services
builder.Services.AddSingleton<HmacValidationService>();
builder.Services.AddSingleton<FacebookPayloadParser>();
builder.Services.AddSingleton<NormalizedEventMapper>();

// Kafka Producer
builder.Services.AddSingleton(sp =>
{
    var bootstrapServers = sp.GetRequiredService<IConfiguration>()["Kafka:BootstrapServers"] ?? "kafka:9092";
    var logger = sp.GetRequiredService<ILogger<RawEventsProducer>>();
    return new RawEventsProducer(bootstrapServers, logger);
});

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck("liveness", () => HealthCheckResult.Healthy("Alive"), tags: new[] { "liveness" })
    .AddCheck("kafka", () =>
    {
        var kafkaServer = builder.Configuration["Kafka:BootstrapServers"] ?? "kafka:9092";
        try
        {
            var adminConfig = new Confluent.Kafka.AdminClientConfig { BootstrapServers = kafkaServer };
            using var admin = new Confluent.Kafka.AdminClientBuilder(adminConfig).Build();
            var meta = admin.GetMetadata(TimeSpan.FromSeconds(5));
            return meta.Brokers.Count > 0
                ? HealthCheckResult.Healthy($"Kafka reachable ({meta.Brokers.Count} brokers)")
                : HealthCheckResult.Unhealthy("No Kafka brokers found");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Kafka unreachable: {ex.Message}");
        }
    }, tags: new[] { "readiness" });

var app = builder.Build();

app.UseRouting();
app.MapControllers();

// Health endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("liveness")
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("readiness")
});

app.Run();
