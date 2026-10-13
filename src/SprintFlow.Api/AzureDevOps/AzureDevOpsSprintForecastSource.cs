using SprintFlow.Timeline;
using SprintFlow.Forecast;

namespace SprintFlow.AzureDevOps;

public sealed class AzureDevOpsSprintForecastSource(AzureDevOpsCliClient client) : ISprintForecastSource
{
    public async Task<IReadOnlyList<SprintSummary>> ListarAsync(CancellationToken cancellationToken = default)
    {
        return (await client.ObterIteracoesAsync(cancellationToken))
            .Where(item => item.Attributes?.StartDate is not null && item.Attributes.FinishDate is not null)
            .Select(ConverterParaResumo)
            .OrderBy(item => item.StartDate)
            .ToList();
    }

    public async Task<SprintForecastDataset?> ObterAsync(string sprintId, CancellationToken cancellationToken = default)
    {
        var iterations = await client.ObterIteracoesAsync(cancellationToken);
        var iteration = iterations.FirstOrDefault(item => string.Equals(item.Id, sprintId, StringComparison.OrdinalIgnoreCase));
        if (iteration is null)
            throw new AzureDevOpsReadException($"A sprint solicitada não foi encontrada entre as {iterations.Count} iterações disponíveis para o time configurado.");
        if (iteration.Path is null || iteration.Attributes?.StartDate is null || iteration.Attributes.FinishDate is null)
            throw new AzureDevOpsReadException("A sprint solicitada não possui caminho ou datas válidos no Azure DevOps.");

        var sprint = ConverterParaResumo(iteration);
        var membersTask = client.ObterMembrosDoTimeAsync(cancellationToken);
        var capacitiesTask = CarregarCapacidadesAsync(iteration.Id, cancellationToken);
        var teamDaysOffTask = client.ObterFolgasDoTimeAsync(iteration.Id, cancellationToken);
        var itemsTask = client.ObterItensAsync(iteration.Path, cancellationToken);
        await Task.WhenAll(membersTask, capacitiesTask, teamDaysOffTask, itemsTask);

        var capacities = capacitiesTask.Result.ToDictionary(item => item.TeamMember.Id, StringComparer.OrdinalIgnoreCase);
        var members = membersTask.Result.Select(item => ConverterParaMembro(item.Identity, capacities.GetValueOrDefault(item.Identity.Id))).ToList();
        var items = itemsTask.Result;
        var storyItems = items.Where(item => EhHistoria(item.Type)).ToDictionary(item => item.Id);
        var taskItems = items.Where(item => EhTarefa(item.Type)).ToList();
        IReadOnlyDictionary<int, IReadOnlyList<TaskActivityPeriod>> activity = new Dictionary<int, IReadOnlyList<TaskActivityPeriod>>();
        if (sprint.Status == "Current")
        {
            var revisions = await client.ObterRevisoesDosItensAsync(taskItems.Select(item => item.Id), cancellationToken);
            activity = revisions.ToDictionary(item => item.Key, item => MontarPeriodosDeAtividade(item.Value));
        }
        foreach (var missing in taskItems.Where(task => task.ParentId is not null && !storyItems.ContainsKey(task.ParentId.Value)).Select(task => task.ParentId!.Value).Distinct())
            storyItems[missing] = new AzureWorkItem(missing, null, "User Story", $"US #{missing}", "Unknown", null, 0, null);

        var stories = storyItems.Values.OrderBy(item => item.Order ?? decimal.MaxValue).ThenBy(item => item.Id)
            .Select((item, index) => new ForecastStory(item.Id, item.Title, item.Order ?? index + 1, NormalizarEstadoDaTarefa(item.State), item.Description)).ToList();
        var tasks = taskItems.Where(item => item.ParentId is not null).Select(item => new ForecastTask(
            item.Id, item.ParentId!.Value, item.Title, item.AssignedToId, ObterEtapa(item, members), item.RemainingWork,
            NormalizarEstadoDaTarefa(item.State), item.Order, item.CreatedAt)).ToList();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var forecastDate = today < sprint.StartDate ? sprint.StartDate : today;
        return new SprintForecastDataset(sprint,
            new ForecastInput(sprint.StartDate, sprint.EndDate, forecastDate, members, stories, tasks),
            items.ToDictionary(item => item.Id, item => client.ObterUrlDoItem(item.Id)), activity,
            TeamDaysOff: teamDaysOffTask.Result.SelectMany(EnumerarDatas).Distinct().OrderBy(date => date).ToList());
    }

    public static IReadOnlyList<TaskActivityPeriod> MontarPeriodosDeAtividade(IReadOnlyList<AzureWorkItemRevision> revisions)
    {
        var changes = new List<AzureWorkItemRevision>();
        foreach (var revision in revisions.OrderBy(item => item.ChangedAt))
        {
            var normalized = revision with { State = NormalizarEstadoDaTarefa(revision.State) };
            if (changes.LastOrDefault() is { } previous &&
                previous.State == normalized.State && previous.AssignedToId == normalized.AssignedToId)
                continue;
            changes.Add(normalized);
        }

        var periods = new List<TaskActivityPeriod>();
        var hasStarted = false;
        for (var index = 0; index < changes.Count; index++)
        {
            var change = changes[index];
            var kind = change.State switch
            {
                "In Progress" => "Active",
                "Done" => "Done",
                "Review" => "Review",
                _ => hasStarted ? "Paused" : "Assigned"
            };
            if (kind is "Active" or "Review" or "Done") hasStarted = true;
            periods.Add(new TaskActivityPeriod(change.AssignedToId, change.AssignedToName, kind,
                change.ChangedAt, index + 1 < changes.Count ? changes[index + 1].ChangedAt : null));
        }
        return periods;
    }

    private static SprintSummary ConverterParaResumo(AzureIteration item)
    {
        // Datas de iteração do Azure DevOps são datas de calendário serializadas em UTC.
        // Convertê-las para o fuso da máquina faz uma sprint que termina à meia-noite UTC
        // parecer encerrada no dia anterior (especialmente no Brasil).
        var start = DateOnly.FromDateTime(item.Attributes!.StartDate!.Value.UtcDateTime);
        var end = DateOnly.FromDateTime(item.Attributes.FinishDate!.Value.UtcDateTime);
        var timeFrame = item.Attributes.TimeFrame?.Trim().ToLowerInvariant();
        var status = timeFrame switch
        {
            "current" => "Current",
            "future" => "Future",
            "past" => "Past",
            _ => start > DateOnly.FromDateTime(DateTime.Today) ? "Future" : end < DateOnly.FromDateTime(DateTime.Today) ? "Past" : "Current"
        };
        return new(item.Id, item.Name, start, end, status);
    }

    private async Task<IReadOnlyList<AzureCapacity>> CarregarCapacidadesAsync(string iterationId, CancellationToken cancellationToken)
    {
        try { return await client.ObterCapacidadesAsync(iterationId, cancellationToken); }
        catch (AzureDevOpsReadException) { return []; }
    }

    private static ForecastMember ConverterParaMembro(AzureIdentity identity, AzureCapacity? capacity)
    {
        var activity = capacity?.Activities.OrderByDescending(item => item.CapacityPerDay).FirstOrDefault();
        var daysOff = capacity?.DaysOff.SelectMany(EnumerarDatas).ToHashSet() ?? [];
        return new(identity.Id, identity.DisplayName, ObterPapel(activity?.Name) ?? SquadRole.PD, activity?.CapacityPerDay ?? 0, daysOff);
    }

    private static IEnumerable<DateOnly> EnumerarDatas(AzureDayOff range)
    {
        for (var date = DateOnly.FromDateTime(range.Start.DateTime); date <= DateOnly.FromDateTime(range.End.DateTime); date = date.AddDays(1)) yield return date;
    }

    private static SquadRole? ObterPapel(string? value) => value?.ToLowerInvariant() switch
    {
        var name when name?.Contains("back") == true => SquadRole.Backend,
        var name when name?.Contains("front") == true => SquadRole.Frontend,
        var name when name?.Contains("qa") == true || name?.Contains("test") == true => SquadRole.QA,
        "pd" => SquadRole.PD,
        "el" => SquadRole.EL,
        "gpm" => SquadRole.GPM,
        "pm" => SquadRole.PM,
        _ => null
    };

    private static WorkStage ObterEtapa(AzureWorkItem item, IReadOnlyList<ForecastMember> members)
    {
        var text = item.Title.ToLowerInvariant();
        // Marcadores explícitos na própria task são mais confiáveis que a função da
        // pessoa: alguém de Backend pode receber uma task [Front], por exemplo.
        if (text.Contains("front") || text.Contains(" ui")) return WorkStage.Frontend;
        if (text.Contains("qa") || text.Contains("test")) return WorkStage.QA;
        if (text.Contains("back") || text.Contains("api")) return WorkStage.Backend;
        var ownerRole = members.FirstOrDefault(member => member.Id == item.AssignedToId)?.Role;
        if (ownerRole == SquadRole.Backend) return WorkStage.Backend;
        if (ownerRole == SquadRole.Frontend) return WorkStage.Frontend;
        if (ownerRole == SquadRole.QA) return WorkStage.QA;
        return WorkStage.Other;
    }

    private static bool EhHistoria(string type) => type.Equals("User Story", StringComparison.OrdinalIgnoreCase) || type.Equals("Product Backlog Item", StringComparison.OrdinalIgnoreCase);
    private static bool EhTarefa(string type) => type.Equals("Task", StringComparison.OrdinalIgnoreCase) || type.Equals("Bug", StringComparison.OrdinalIgnoreCase);
    private static string NormalizarEstadoDaTarefa(string state) => state.ToLowerInvariant() switch
    {
        "new" or "to do" or "todo" => "Todo",
        "active" or "in progress" or "doing" => "In Progress",
        "resolved" or "review" => "Review",
        "closed" or "done" => "Done",
        _ => state
    };
}
