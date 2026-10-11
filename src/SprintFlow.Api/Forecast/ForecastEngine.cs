namespace SprintFlow.Forecast;

public sealed class ForecastEngine
{
    public ForecastResult CalcularPrevisao(ForecastInput input)
    {
        var members = input.Members.ToDictionary(member => member.Id);
        var storyOrder = input.Stories.ToDictionary(story => story.Id, story => story.Order);
        var cursors = input.Members.ToDictionary(
            member => member.Id,
            member => new MemberCursor(input.ForecastDate, member.DailyCapacityHours));
        var forecasts = new Dictionary<int, TaskForecast>();
        var blockages = new List<FlowBlockage>();
        var ignored = new List<int>();

        var tasks = input.Tasks
            .OrderBy(task => ObterOrdemDaEtapa(task.Stage))
            .ThenBy(task => storyOrder.GetValueOrDefault(task.StoryId, decimal.MaxValue))
            .ThenBy(task => task.Order ?? decimal.MaxValue)
            .ThenBy(task => task.Id)
            .ToList();

        foreach (var task in tasks)
        {
            if (EstaConcluida(task))
            {
                forecasts[task.Id] = new TaskForecast(task.Id, task.StoryId, task.Title,
                    task.AssignedMemberId ?? string.Empty, task.Stage, task.State, [], null, null, false);
                continue;
            }

            if (task.AssignedMemberId is null || !members.TryGetValue(task.AssignedMemberId, out var member) ||
                task.RemainingWork <= 0 || member.DailyCapacityHours <= 0 ||
                !cursors.TryGetValue(task.AssignedMemberId, out var cursor))
            {
                ignored.Add(task.Id);
                continue;
            }

            var segments = new List<AllocationSegment>();
            var related = input.Tasks.Where(item => item.StoryId == task.StoryId).ToList();
            if (task.Stage == WorkStage.Frontend)
            {
                var backend = related.Where(item => item.Stage == WorkStage.Backend && !EstaConcluida(item)).ToList();
                var backendEnd = backend.Count == 0 ? (Date: cursor.Date, TaskId: (int?)null) : ObterUltimoTermino(backend, forecasts);
                var backendAvailable = backend.Count == 0 ? cursor.Date : backendEnd.Date.AddDays(1);
                AdicionarBloqueios(blockages, cursor, member, task, backendAvailable, backendEnd.TaskId,
                    "Frontend aguardando conclusão do Backend.");
                segments.AddRange(Alocar(cursor, member, task.RemainingWork, backendAvailable));
            }
            else if (task.Stage == WorkStage.QA)
            {
                var development = related.Where(item => item.Stage is WorkStage.Backend or WorkStage.Frontend && !EstaConcluida(item)).ToList();
                var developmentEnd = development.Count == 0 ? (Date: cursor.Date, TaskId: (int?)null) : ObterUltimoTermino(development, forecasts);
                var qaAvailable = development.Count == 0 ? cursor.Date : developmentEnd.Date.AddDays(1);
                AdicionarBloqueios(blockages, cursor, member, task, qaAvailable, developmentEnd.TaskId,
                    "QA aguardando todas as tasks de Backend e Frontend da User Story.");
                segments.AddRange(Alocar(cursor, member, task.RemainingWork, qaAvailable));
            }
            else
            {
                segments.AddRange(Alocar(cursor, member, task.RemainingWork, cursor.Date));
            }

            DateOnly? start = segments.Count == 0 ? null : segments.Min(segment => segment.Date);
            DateOnly? end = segments.Count == 0 ? null : segments.Max(segment => segment.Date);
            forecasts[task.Id] = new TaskForecast(task.Id, task.StoryId, task.Title, member.Id, task.Stage,
                task.State, segments, start, end, end > input.SprintEnd);
        }

        return new ForecastResult(
            forecasts.Values.OrderBy(task => task.StartDate).ThenBy(task => task.Id).ToList(),
            blockages.OrderBy(item => item.Date).ThenBy(item => item.AffectedMemberId).ToList(),
            ignored);
    }

    private static IReadOnlyList<AllocationSegment> Alocar(
        MemberCursor cursor,
        ForecastMember member,
        decimal requestedHours,
        DateOnly notBefore)
    {
        var segments = new List<AllocationSegment>();
        var remaining = requestedHours;
        if (cursor.Date < notBefore)
        {
            cursor.Date = notBefore;
            cursor.RemainingHours = cursor.DailyCapacity;
        }

        while (remaining > 0)
        {
            AvancarParaDiaUtil(cursor, member);
            var hours = Math.Min(remaining, cursor.RemainingHours);
            segments.Add(new AllocationSegment(cursor.Date, hours));
            remaining -= hours;
            cursor.RemainingHours -= hours;
            if (cursor.RemainingHours == 0)
            {
                cursor.Date = cursor.Date.AddDays(1);
                cursor.RemainingHours = cursor.DailyCapacity;
            }
        }

        return segments;
    }

    private static void AdicionarBloqueios(
        ICollection<FlowBlockage> output,
        MemberCursor cursor,
        ForecastMember member,
        ForecastTask affectedTask,
        DateOnly dependencyEnd,
        int? blockingTaskId,
        string cause)
    {
        var date = cursor.Date;
        var available = cursor.RemainingHours;
        while (date < dependencyEnd)
        {
            if (EhDiaUtil(date, member) && available > 0)
                output.Add(new FlowBlockage(member.Id, affectedTask.Id, blockingTaskId, date, available, cause));
            date = date.AddDays(1);
            available = member.DailyCapacityHours;
        }
    }

    private static (DateOnly Date, int? TaskId) ObterUltimoTermino(
        IReadOnlyList<ForecastTask> dependencies,
        IReadOnlyDictionary<int, TaskForecast> forecasts)
    {
        var latest = dependencies
            .Select(task => forecasts.TryGetValue(task.Id, out var forecast) && forecast.EndDate is not null
                ? (Date: forecast.EndDate.Value, TaskId: (int?)task.Id)
                : (Date: DateOnly.MinValue, TaskId: (int?)null))
            .OrderByDescending(item => item.Date)
            .FirstOrDefault();
        return latest.Date == DateOnly.MinValue ? (DateOnly.MinValue, null) : latest;
    }

    private static void AvancarParaDiaUtil(MemberCursor cursor, ForecastMember member)
    {
        while (!EhDiaUtil(cursor.Date, member))
        {
            cursor.Date = cursor.Date.AddDays(1);
            cursor.RemainingHours = cursor.DailyCapacity;
        }
    }

    private static bool EhDiaUtil(DateOnly date, ForecastMember member) =>
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday &&
        !(member.DaysOff?.Contains(date) ?? false);

    private static bool EstaConcluida(ForecastTask task) =>
        string.Equals(task.State, "Done", StringComparison.OrdinalIgnoreCase);

    private static int ObterOrdemDaEtapa(WorkStage stage) => stage switch
    {
        WorkStage.Backend => 0,
        WorkStage.Frontend => 1,
        WorkStage.QA => 2,
        _ => 3
    };

    private sealed class MemberCursor(DateOnly date, decimal dailyCapacity)
    {
        public DateOnly Date { get; set; } = date;
        public decimal DailyCapacity { get; } = dailyCapacity;
        public decimal RemainingHours { get; set; } = dailyCapacity;
    }
}
