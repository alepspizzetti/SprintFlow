using System.Diagnostics;
using System.Text.Json;

namespace SprintFlow.AzureDevOps;

public sealed class AzureDevOpsCliClient(AzureDevOpsOptions options)
{
    private const int MaxConcurrentWorkItemReads = 6;
    private AzureDevOpsOptions Options => options;
    public string WorkItemUrl(int workItemId) => $"{OrganizationUrl()}/{Uri.EscapeDataString(Options.Project)}/_workitems/edit/{workItemId}";
    public async Task<AzureDevOpsDiagnostic> DiagnoseAsync(CancellationToken ct = default)
    {
        var cli = await TryExecuteAsync(["version", "--output", "json"], ct);
        if (!cli.Success) return new(false, false, false, Options.IsConfigured, "Azure CLI não foi encontrado.");
        var extension = await TryExecuteAsync(["extension", "show", "--name", "azure-devops", "--output", "json"], ct);
        if (!extension.Success) return new(true, false, false, Options.IsConfigured, "Extensão azure-devops não está instalada.");
        var auth = await TryExecuteAsync(["account", "show", "--output", "json"], ct);
        return new(true, true, auth.Success, Options.IsConfigured,
            !Options.IsConfigured ? "Informe organização, projeto e time." : auth.Success ? "Pronto para consulta." : "Faça login no Azure CLI.");
    }

    public async Task<IReadOnlyList<AzureIteration>> GetIterationsAsync(CancellationToken ct = default) =>
        AzureDevOpsJsonParser.ReadList<AzureIteration>(await ExecuteAsync(["boards", "iteration", "team", "list", "--organization", OrganizationUrl(), "--project", Options.Project, "--team", Options.Team, "--output", "json"], ct));

    public async Task<IReadOnlyList<AzureTeamMember>> GetTeamMembersAsync(CancellationToken ct = default) =>
        AzureDevOpsJsonParser.ReadList<AzureTeamMember>(await ExecuteAsync(["devops", "team", "list-member", "--organization", OrganizationUrl(), "--project", Options.Project, "--team", Options.Team, "--output", "json"], ct));

    public async Task<IReadOnlyList<AzureCapacity>> GetCapacitiesAsync(string iterationId, CancellationToken ct = default) =>
        AzureDevOpsJsonParser.ReadList<AzureCapacity>(await ExecuteAsync(["devops", "invoke", "--organization", OrganizationUrl(), "--area", "work", "--resource", "capacities", "--route-parameters", $"project={Options.Project}", $"team={Options.Team}", $"iterationId={iterationId}", "--api-version", "7.1", "--output", "json"], ct));

    public async Task<IReadOnlyList<AzureWorkItem>> GetWorkItemsAsync(string iterationPath, CancellationToken ct = default)
    {
        var safePath = iterationPath.Replace("'", "''", StringComparison.Ordinal);
        var wiql = $"SELECT [System.Id] FROM WorkItems WHERE [System.IterationPath] = '{safePath}' ORDER BY [Microsoft.VSTS.Common.StackRank]";
        var references = AzureDevOpsJsonParser.ReadList<AzureWorkItemReference>(await ExecuteAsync(["boards", "query", "--organization", OrganizationUrl(), "--wiql", wiql, "--output", "json"], ct));
        var result = new AzureWorkItem?[references.Count];
        await Parallel.ForEachAsync(Enumerable.Range(0, references.Count), new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrentWorkItemReads,
            CancellationToken = ct
        }, async (index, token) =>
        {
            var reference = references[index];
            var json = await ExecuteAsync(["boards", "work-item", "show", "--id", reference.Id.ToString(), "--organization", OrganizationUrl(), "--expand", "none", "--fields", "System.Id,System.Parent,System.WorkItemType,System.Title,System.State,System.AssignedTo,System.ChangedDate,Microsoft.VSTS.Scheduling.RemainingWork,Microsoft.VSTS.Scheduling.OriginalEstimate,Microsoft.VSTS.Common.StackRank,Microsoft.VSTS.Common.BacklogPriority", "--output", "json"], token);
            result[index] = AzureDevOpsJsonParser.ReadWorkItem(json);
        });
        return result.OfType<AzureWorkItem>().ToList();
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<AzureWorkItemRevision>>> GetWorkItemRevisionsAsync(
        IEnumerable<int> workItemIds, CancellationToken ct = default)
    {
        var ids = workItemIds.Distinct().ToArray();
        var result = new IReadOnlyList<AzureWorkItemRevision>?[ids.Length];
        await Parallel.ForEachAsync(Enumerable.Range(0, ids.Length), new ParallelOptions
        {
            MaxDegreeOfParallelism = MaxConcurrentWorkItemReads,
            CancellationToken = ct
        }, async (index, token) =>
        {
            var json = await ExecuteAsync(["devops", "invoke", "--organization", OrganizationUrl(),
                "--area", "wit", "--resource", "revisions", "--route-parameters", $"id={ids[index]}",
                "--api-version", "7.1", "--output", "json"], token);
            result[index] = AzureDevOpsJsonParser.ReadRevisions(json);
        });
        return ids.Select((id, index) => (id, revisions: result[index] ?? []))
            .ToDictionary(item => item.id, item => item.revisions);
    }

    private string OrganizationUrl() => Options.Organization.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Options.Organization.TrimEnd('/') : $"https://dev.azure.com/{Options.Organization.Trim('/')}";

    private async Task<string> ExecuteAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Options.CommandTimeoutSeconds, 5, 300)));
        var result = await TryExecuteAsync(args, timeout.Token);
        if (!result.Success) throw new AzureDevOpsReadException(result.Error);
        return result.Output;
    }

    private async Task<(bool Success, string Output, string Error)> TryExecuteAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var start = new ProcessStartInfo(Options.ExecutablePath) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment["AZURE_CORE_ONLY_SHOW_ERRORS"] = "true";
        foreach (var arg in args) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return (false, "", "Não foi possível iniciar o Azure CLI.");
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return (process.ExitCode == 0, await output, (await error).Trim());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return (false, "", ex is OperationCanceledException ? "A consulta excedeu o tempo limite." : "Azure CLI não encontrado.");
        }
    }

}

public static class AzureDevOpsJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<T> ReadList<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var items = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("value", out var value) ? value : root.TryGetProperty("workItems", out var workItems) ? workItems : root.GetProperty("teamMembers");
        return items.Deserialize<T[]>(JsonOptions) ?? [];
    }

    public static AzureWorkItem ReadWorkItem(string json)
    {
        using var document = JsonDocument.Parse(json);
        var fields = document.RootElement.GetProperty("fields");
        string? Text(string name) => fields.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString() : null;
        decimal? Number(string name) => fields.TryGetProperty(name, out var value) && value.TryGetDecimal(out var result) ? result : null;
        int? Integer(string name) => fields.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
        string? Identity(string name) => fields.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("id", out var id) ? id.GetString() : null;
        var changedText = Text("System.ChangedDate");
        var changedDate = DateTimeOffset.TryParse(changedText, out var parsedChangedDate) ? parsedChangedDate : (DateTimeOffset?)null;
        return new(Integer("System.Id") ?? 0, Integer("System.Parent"), Text("System.WorkItemType") ?? "Unknown", Text("System.Title") ?? "", Text("System.State") ?? "Unknown", Identity("System.AssignedTo"), Number("Microsoft.VSTS.Scheduling.RemainingWork") ?? Number("Microsoft.VSTS.Scheduling.OriginalEstimate") ?? 0, Number("Microsoft.VSTS.Common.StackRank") ?? Number("Microsoft.VSTS.Common.BacklogPriority"), changedDate);
    }

    public static IReadOnlyList<AzureWorkItemRevision> ReadRevisions(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var values = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("value");
        var revisions = new List<AzureWorkItemRevision>();
        foreach (var item in values.EnumerateArray())
        {
            if (!item.TryGetProperty("fields", out var fields) ||
                !fields.TryGetProperty("System.State", out var state) ||
                !fields.TryGetProperty("System.ChangedDate", out var changed) ||
                !DateTimeOffset.TryParse(changed.GetString(), out var changedAt))
                continue;
            string? memberId = null;
            string? memberName = null;
            if (fields.TryGetProperty("System.AssignedTo", out var assigned))
            {
                if (assigned.ValueKind == JsonValueKind.Object)
                {
                    memberId = assigned.TryGetProperty("id", out var id) ? id.GetString() : null;
                    memberName = assigned.TryGetProperty("displayName", out var name) ? name.GetString() : null;
                }
                else if (assigned.ValueKind == JsonValueKind.String)
                    memberName = assigned.GetString();
            }
            revisions.Add(new(state.GetString() ?? "", changedAt, memberId, memberName));
        }
        return revisions;
    }
}
