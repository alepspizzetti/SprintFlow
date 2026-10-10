using System.Collections.Concurrent;
using System.Text.Json;
using SprintFlow.Timeline;

namespace SprintFlow.AzureDevOps;

public sealed class MemorySprintPlanningSource(AzureDevOpsSprintPlanningSource azure, AzureDevOpsOptions options,
    ILogger<MemorySprintPlanningSource> logger) : ISprintPlanningSource, ISprintPlanningRefresher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, SprintPlanningDataset> snapshots = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> sprintLocks = new();
    private readonly SemaphoreSlim catalogLock = new(1, 1);
    private readonly string cacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SprintFlow", string.Join("_", options.Team.Split(Path.GetInvalidFileNameChars())));
    private IReadOnlyList<SprintSummary>? catalog;

    private string CatalogPath => Path.Combine(cacheDirectory, "sprints.json");
    private string SprintPath(string sprintId) => Path.Combine(cacheDirectory, "sprint-" + sprintId + ".json");

    public async Task<IReadOnlyList<SprintSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (catalog is not null) return catalog;
        await catalogLock.WaitAsync(cancellationToken);
        try
        {
            if (catalog is null)
            {
                catalog = await ReadAsync<List<SprintSummary>>(CatalogPath, cancellationToken);
                if (catalog is null) catalog = await FetchCatalogAsync(cancellationToken);
                else _ = RefreshCatalogAsync();
                _ = WarmAsync(catalog);
            }
            return catalog;
        }
        finally { catalogLock.Release(); }
    }

    public async Task<SprintPlanningDataset?> GetAsync(string sprintId, CancellationToken cancellationToken = default)
    {
        if (snapshots.TryGetValue(sprintId, out var snapshot)) return snapshot;
        var gate = sprintLocks.GetOrAdd(sprintId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (snapshots.TryGetValue(sprintId, out snapshot)) return snapshot;
            snapshot = await ReadAsync<SprintPlanningDataset>(SprintPath(sprintId), cancellationToken)
                ?? await FetchSprintAsync(sprintId, cancellationToken);
            if (snapshot is not null) snapshots[sprintId] = snapshot;
            return snapshot;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> RefreshAsync(string sprintId, CancellationToken cancellationToken = default)
    {
        var gate = sprintLocks.GetOrAdd(sprintId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var fresh = await FetchSprintAsync(sprintId, cancellationToken);
            if (fresh is null) return false;
            snapshots[sprintId] = fresh;
            return true;
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<SprintSummary>> FetchCatalogAsync(CancellationToken cancellationToken)
    {
        var fresh = (await azure.ListAsync(cancellationToken))
            .Where(sprint => sprint.Status is "Current" or "Future")
            .ToList();
        await WriteAsync(CatalogPath, fresh, cancellationToken);
        return fresh;
    }

    private async Task RefreshCatalogAsync()
    {
        try { catalog = await FetchCatalogAsync(CancellationToken.None); }
        catch (Exception exception) { logger.LogWarning(exception, "Falha ao atualizar o catálogo de sprints em segundo plano."); }
    }

    private async Task<SprintPlanningDataset?> FetchSprintAsync(string sprintId, CancellationToken cancellationToken)
    {
        var fresh = await azure.GetAsync(sprintId, cancellationToken);
        if (fresh is null) return null;
        fresh = fresh with { SyncedAt = DateTimeOffset.Now };
        await WriteAsync(SprintPath(sprintId), fresh, cancellationToken);
        return fresh;
    }

    private async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            logger.LogWarning(exception, "Cache local ignorado: {Path}", path);
            return null;
        }
    }

    private async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            var temporary = path + ".tmp";
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Não foi possível gravar o cache local: {Path}", path);
        }
    }

    private async Task WarmAsync(IReadOnlyList<SprintSummary> sprints)
    {
        foreach (var sprint in sprints.OrderBy(item => item.Status == "Current" ? 0 : 1).ThenBy(item => item.StartDate))
        {
            try { await GetAsync(sprint.Id); }
            catch (Exception) { /* The page or manual sync can retry this sprint. */ }
        }
    }
}
