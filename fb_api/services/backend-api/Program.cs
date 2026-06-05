using FbApi.BackendApi.Repositories;
using FbApi.BackendApi.Services;
using FbApi.BackendApi.Workers;
using Microsoft.Extensions.Http.Resilience;
using Polly;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

builder.Services.AddControllers();

builder.Services.AddSingleton<IIdempotencyRepository, IdempotencyRepository>();
builder.Services.AddSingleton<ICommandStatusRepository, CommandStatusRepository>();
builder.Services.AddSingleton<IFacebookApiErrorHandler, FacebookApiErrorHandler>();
builder.Services.AddSingleton<ISendFailedPublisher, SendFailedPublisher>();

builder.Services.AddHttpClient<IFacebookActionService, FacebookActionService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler(options =>
{
    options.Retry.MaxRetryAttempts = 3;
    options.Retry.Delay = TimeSpan.FromMilliseconds(500);
    options.Retry.BackoffType = DelayBackoffType.Exponential;
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 10;
    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHttpClient("FacebookApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddStandardResilienceHandler();

builder.Services.AddHostedService<ReplyCommandWorker>();
builder.Services.AddHostedService<SendRetryWorker>();

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgresql",
        tags: new[] { "ready" });

var app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
