using FbApi.CoreService.Controllers;
using FbApi.CoreService.Services;
using FbApi.CoreService.Workers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:3002");

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

builder.Services.AddControllers()
    .AddApplicationPart(typeof(HealthController).Assembly);

builder.Services.AddSingleton<ISpamDetectorService, SpamDetectorService>();
builder.Services.AddSingleton<SpamTrackerService>();
builder.Services.AddSingleton<ISpamTrackerService>(sp => sp.GetRequiredService<SpamTrackerService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SpamTrackerService>());
builder.Services.AddSingleton<IEventStatusTracker, EventStatusTracker>();
builder.Services.AddSingleton<IReplyCommandPublisher, ReplyCommandPublisher>();

builder.Services.AddHttpClient<IGeminiService, GeminiService>();

builder.Services.AddSingleton<IRuleEngineService, RuleEngineService>();

builder.Services.AddHostedService<CoreEventWorker>();

builder.Services.AddHealthChecks()
    .AddCheck("liveness", () => HealthCheckResult.Healthy("Alive"), tags: new[] { "liveness" });

var app = builder.Build();

app.UseRouting();
app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("liveness")
});

app.Run();
