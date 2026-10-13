using System.Diagnostics;
using System.Text.Json;

namespace SprintFlow.AzureDevOps;

public sealed class AzureDevOpsCliClient(AzureDevOpsOptions options)
{
    private const int MaxConcurrentWorkItemReads = 6;
    private AzureDevOpsOptions Options => options;
    public string ObterUrlDoItem(int workItemId) => $"{ObterUrlDaOrganizacao()}/{Uri.EscapeDataString(Options.Project)}/_workitems/edit/{workItemId}";
    public async Task<AzureDevOpsDiagnostic> DiagnosticarAsync(CancellationToken ct = default)
    {
        var cli = await TentarExecutarAsync(["version", "--output", "json"], ct);
        if (!cli.Success) return new(false, false, false, Options.IsConfigured, "Azure CLI não foi encontrado.");
        var extension = await TentarExecutarAsync(["extension", "show", "--name", "azure-devops", "--output", "json"], ct);
        if (!extension.Success) return new(true, false, false, Options.IsConfigured, "Extensão azure-devops não está instalada.");
        var auth = await TentarExecutarAsync(["account", "show", "--output", "json"], ct);
        return new(true, true, auth.Success, Options.IsConfigured,
            !Options.IsConfigured ? "Informe organização, projeto e time." : auth.Success ? "Pronto para consulta." : "Faça login no Azure CLI.");
    }

    public async Task<IReadOnlyList<AzureIteration>> ObterIteracoesAsync(CancellationToken ct = default) =>
        AzureDevOpsJsonParser.LerLista<AzureIteration>(await ExecutarAsync(["boards", "iteration", "team", "list", "--organization", ObterUrlDaOrganizacao(), "--project", Options.Project, "--team", Options.Team, "--output", "json"], ct));

    public async Task<IReadOnlyList<AzureTeamMember>> ObterMembrosDoTimeAsync(CancellationToken ct = default) =>
        AzureDevOpsJsonParser.LerLista<AzureTeamMember>(await ExecutarAsync(["devops", "team", "list-member", "--organization", ObterUrlDaOrganizacao(), "--project", Options.Project, "--team", Options.Team, "--output", "json"], ct));

    public async Task<IReadOnlyList<AzureCapacity>> ObterCapacidadesAsync(string iterationId, CancellationToken ct = default) =>
        AzureDevOpsJsonParser.LerLista<AzureCapacity>(await ExecutarAsync(["devops", "invoke", "--organization", ObterUrlDaOrganizacao(), "--area", "work", "--resource", "capacities", "--route-parameters", $"project={Options.Project}", $"team={Options.Team}", $"iterationId={iterationId}", "--api-version", "7.1", "--output", "json"], ct));

    public async Task<IReadOnlyList<AzureDayOff>> ObterFolgasDoTimeAsync(string iterationId, CancellationToken ct = default) =>
        AzureDevOpsJsonParser.LerLista<AzureDayOff>(await ExecutarAsync(["devops", "invoke", "--organization", ObterUrlDaOrganizacao(), "--area", "work", "--resource", "teamdaysoff", "--route-parameters", $"project={Options.Project}", $"team={Options.Team}", $"iterationId={iterationId}", "--api-version", "7.1", "--http-method", "GET", "--output", "json"], ct));

    public async Task<IReadOnlyList<AzureWorkItem>> ObterItensAsync(string iterationPath, CancellationToken ct = default)
    {
        var safePath = iterationPath.Replace("'", "''", StringComparison.Ordinal);
        var wiql = $"SELECT [System.Id], [System.Parent], [System.WorkItemType], [System.Title], [System.Description], [System.State], [System.AssignedTo], [System.CreatedDate], [Microsoft.VSTS.Scheduling.RemainingWork], [Microsoft.VSTS.Scheduling.OriginalEstimate], [Microsoft.VSTS.Common.StackRank], [Microsoft.VSTS.Common.BacklogPriority] FROM WorkItems WHERE [System.IterationPath] = '{safePath}' ORDER BY [Microsoft.VSTS.Common.StackRank]";
        return AzureDevOpsJsonParser.LerItens(await ExecutarAsync(["boards", "query", "--organization", ObterUrlDaOrganizacao(), "--wiql", wiql, "--output", "json"], ct));
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlyList<AzureWorkItemRevision>>> ObterRevisoesDosItensAsync(
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
            var json = await ExecutarAsync(["devops", "invoke", "--organization", ObterUrlDaOrganizacao(),
                "--area", "wit", "--resource", "revisions", "--route-parameters", $"id={ids[index]}",
                "--api-version", "7.1", "--output", "json"], token);
            result[index] = AzureDevOpsJsonParser.LerRevisoes(json);
        });
        return ids.Select((id, index) => (id, revisions: result[index] ?? []))
            .ToDictionary(item => item.id, item => item.revisions);
    }

    private string ObterUrlDaOrganizacao() => Options.Organization.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? Options.Organization.TrimEnd('/') : $"https://dev.azure.com/{Options.Organization.Trim('/')}";

    private async Task<string> ExecutarAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(Options.CommandTimeoutSeconds, 5, 300)));
        var result = await TentarExecutarAsync(args, timeout.Token);
        if (!result.Success) throw new AzureDevOpsReadException(result.Error);
        return result.Output;
    }

    private async Task<(bool Success, string Output, string Error)> TentarExecutarAsync(IReadOnlyList<string> args, CancellationToken ct)
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

    public static IReadOnlyList<T> LerLista<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var items = root.ValueKind == JsonValueKind.Array ? root : root.TryGetProperty("value", out var value) ? value : root.TryGetProperty("workItems", out var workItems) ? workItems : root.TryGetProperty("daysOff", out var daysOff) ? daysOff : root.GetProperty("teamMembers");
        return items.Deserialize<T[]>(JsonOptions) ?? [];
    }

    public static IReadOnlyList<AzureWorkItem> LerItens(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Null ? [] :
            document.RootElement.EnumerateArray().Select(LerItem).ToList();
    }

    private static AzureWorkItem LerItem(JsonElement item)
    {
        var fields = item.GetProperty("fields");
        string? Texto(string name) => fields.TryGetProperty(name, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString() : null;
        decimal? Numero(string name) => fields.TryGetProperty(name, out var value) && value.TryGetDecimal(out var result) ? result : null;
        int? Inteiro(string name) => fields.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
        string? Identidade(string name) => fields.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object && value.TryGetProperty("id", out var id) ? id.GetString() : null;
        return new(Inteiro("System.Id") ?? 0, Inteiro("System.Parent"), Texto("System.WorkItemType") ?? "Unknown", Texto("System.Title") ?? "", Texto("System.State") ?? "Unknown", Identidade("System.AssignedTo"), Numero("Microsoft.VSTS.Scheduling.RemainingWork") ?? Numero("Microsoft.VSTS.Scheduling.OriginalEstimate") ?? 0, Numero("Microsoft.VSTS.Common.StackRank") ?? Numero("Microsoft.VSTS.Common.BacklogPriority"), Texto("System.Description"),
            DateTimeOffset.TryParse(Texto("System.CreatedDate"), out var createdAt) ? createdAt : null);
    }

    public static IReadOnlyList<AzureWorkItemRevision> LerRevisoes(string json)
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
