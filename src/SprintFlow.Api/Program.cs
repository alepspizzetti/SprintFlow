using System.Text.Json.Serialization;
using SprintFlow.Timeline;
using SprintFlow.Planning;
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
builder.Services.AddSingleton<AzureDevOpsSprintPlanningSource>();
builder.Services.AddSingleton<ISprintPlanningSource, MemorySprintPlanningSource>();
builder.Services.AddSingleton<ObjectivePlanningEngine>();
builder.Services.AddSingleton<TimelineService>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", async (AzureDevOpsCliClient client, CancellationToken cancellationToken) =>
    Results.Ok(await client.DiagnoseAsync(cancellationToken)));
app.MapGet("/api/settings", (AzureDevOpsOptions options) => Results.Ok(new { options.Team }));
app.MapGet("/api/sprints", async (TimelineService service, AzureDevOpsOptions options, CancellationToken cancellationToken) =>
    options.IsConfigured
        ? Results.Ok(await service.ListSprintsAsync(cancellationToken))
        : Results.Problem("Configure AzureDevOps em appsettings.Local.json.", statusCode: 503));
app.MapGet("/api/sprints/{sprintId}/timeline", async (string sprintId, TimelineService service, CancellationToken cancellationToken) =>
    await service.GetAsync(sprintId, cancellationToken) is { } timeline ? Results.Ok(timeline) : Results.NotFound());
app.MapPost("/api/sprints/{sprintId}/sync", async (string sprintId, TimelineService service, CancellationToken cancellationToken) =>
    await service.SyncAsync(sprintId, cancellationToken) is { } timeline ? Results.Ok(timeline) : Results.NotFound());
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program;
