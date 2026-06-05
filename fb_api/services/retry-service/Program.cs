using FbApi.RetryService.Services;
using FbApi.RetryService.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

builder.Services.AddControllers();

builder.Services.AddSingleton<IRetryPolicyService, RetryPolicyService>();
builder.Services.AddSingleton<ISendRetryPublisher, SendRetryPublisher>();
builder.Services.AddSingleton<IDeadLetterPublisher, DeadLetterPublisher>();

builder.Services.AddHostedService<RetryWorker>();

var app = builder.Build();

app.MapControllers();

app.Run();
