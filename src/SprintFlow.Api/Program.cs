using System.Text.Json.Serialization;
using SprintFlow.Timeline;
using SprintFlow.Forecast;
using SprintFlow.AzureDevOps;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = "wwwroot"
});
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"), optional: true, reloadOnChange: true);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(builder.Configuration.GetSection("AzureDevOps").Get<AzureDevOpsOptions>() ?? new AzureDevOpsOptions());
builder.Services.AddSingleton<AzureDevOpsCliClient>();
builder.Services.AddSingleton<AzureDevOpsSprintForecastSource>();
builder.Services.AddSingleton<ISprintForecastSource, MemorySprintForecastSource>();
builder.Services.AddSingleton(builder.Configuration.GetSection("Forecast").Get<ForecastOptions>() ?? new ForecastOptions());
builder.Services.AddSingleton<ForecastEngine>();
builder.Services.AddSingleton<TimelineService>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", async (AzureDevOpsCliClient client, CancellationToken cancellationToken) =>
    Results.Ok(await client.DiagnosticarAsync(cancellationToken)));
app.MapGet("/api/settings", (AzureDevOpsOptions options) => Results.Ok(new { options.Team }));
app.MapGet("/api/sprints", async (TimelineService service, AzureDevOpsOptions options, CancellationToken cancellationToken) =>
    options.IsConfigured
        ? Results.Ok(await service.ListarSprintsAsync(cancellationToken))
        : Results.Problem("Configure AzureDevOps em appsettings.Local.json.", statusCode: 503));
app.MapGet("/api/sprints/{sprintId}/timeline", async (string sprintId, TimelineService service, CancellationToken cancellationToken) =>
    await service.ObterAsync(sprintId, cancellationToken) is { } timeline ? Results.Ok(timeline) : Results.NotFound());
app.MapPost("/api/sprints/{sprintId}/sync", async (string sprintId, TimelineService service, CancellationToken cancellationToken) =>
    await service.SincronizarAsync(sprintId, cancellationToken) is { } timeline ? Results.Ok(timeline) : Results.NotFound());
app.MapFallbackToFile("index.html");
app.Run();
