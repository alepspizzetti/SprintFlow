namespace SprintFlow.Forecast;

public sealed class ForecastEngine(ForecastOptions? options = null)
{
    private readonly ForecastOptions workday = ValidarExpediente(options ?? new ForecastOptions());

    public ForecastResult CalcularPrevisao(ForecastInput input)
    {
        var members = input.Members.ToDictionary(member => member.Id);
        var storyOrder = input.Stories.ToDictionary(story => story.Id, story => story.Order);
        var cursors = input.Members.Where(member => member.DailyCapacityHours > 0).ToDictionary(
            member => member.Id,
            member => CriarCursor(input, member));
        var forecasts = new Dictionary<int, TaskForecast>();
        var blockages = new List<FlowBlockage>();
        var ignored = new List<int>();

        var tasks = input.Tasks
            .OrderBy(task => EstaEmAndamento(task) ? 0 : 1)
            .ThenBy(task => ObterOrdemDaEtapa(task.Stage))
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
            if (EstaEmAndamento(task))
            {
                var progress = CriarCursor(input, member);
                segments.AddRange(Alocar(progress, member, task.RemainingWork, DateTime.MinValue));
                MoverPara(cursor, ObterInstante(progress));
            }
            else if (task.Stage == WorkStage.Frontend)
            {
                MoverPara(cursor, task.CreatedAt?.LocalDateTime ?? DateTime.MinValue);
                var backend = related.Where(item => item.Stage == WorkStage.Backend && !EstaConcluida(item)).ToList();
                var backendEnd = ObterUltimoTermino(backend, forecasts);
                AdicionarBloqueios(blockages, cursor, member, task, backendEnd.End, backendEnd.TaskId,
                    "Frontend aguardando conclusão do Backend.");
                segments.AddRange(Alocar(cursor, member, task.RemainingWork, backendEnd.End));
            }
            else if (task.Stage == WorkStage.QA)
            {
                MoverPara(cursor, task.CreatedAt?.LocalDateTime ?? DateTime.MinValue);
                var development = related.Where(item => item.Stage is WorkStage.Backend or WorkStage.Frontend && !EstaConcluida(item)).ToList();
                var developmentEnd = ObterUltimoTermino(development, forecasts);
                AdicionarBloqueios(blockages, cursor, member, task, developmentEnd.End, developmentEnd.TaskId,
                    "QA aguardando todas as tasks de Backend e Frontend da User Story.");
                segments.AddRange(Alocar(cursor, member, task.RemainingWork, developmentEnd.End));
            }
            else
            {
                segments.AddRange(Alocar(cursor, member, task.RemainingWork, task.CreatedAt?.LocalDateTime ?? DateTime.MinValue));
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

    private IReadOnlyList<AllocationSegment> Alocar(
        MemberCursor cursor,
        ForecastMember member,
        decimal requestedHours,
        DateTime notBefore)
    {
        var segments = new List<AllocationSegment>();
        var remaining = requestedHours;
        MoverPara(cursor, notBefore);

        while (remaining > 0)
        {
            AvancarParaDiaUtil(cursor, member);
            var hours = Math.Min(remaining, cursor.RemainingHours);
            var used = cursor.DailyCapacity - cursor.RemainingHours;
            segments.Add(new AllocationSegment(cursor.Date, hours,
                ObterInstante(cursor.Date, used, cursor.DailyCapacity),
                ObterInstante(cursor.Date, used + hours, cursor.DailyCapacity)));
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

    private void AdicionarBloqueios(
        ICollection<FlowBlockage> output,
        MemberCursor cursor,
        ForecastMember member,
        ForecastTask affectedTask,
        DateTime dependencyEnd,
        int? blockingTaskId,
        string cause)
    {
        var limit = new MemberCursor(cursor.Date, cursor.DailyCapacity) { RemainingHours = cursor.RemainingHours };
        MoverPara(limit, dependencyEnd);
        var date = cursor.Date;
        var available = cursor.RemainingHours;
        while (date <= limit.Date)
        {
            var blocked = available - (date == limit.Date ? limit.RemainingHours : 0);
            if (EhDiaUtil(date, member) && blocked > 0)
                output.Add(new FlowBlockage(member.Id, affectedTask.Id, blockingTaskId, date, blocked, cause));
            date = date.AddDays(1);
            available = member.DailyCapacityHours;
        }
    }

    private static (DateTime End, int? TaskId) ObterUltimoTermino(
        IReadOnlyList<ForecastTask> dependencies,
        IReadOnlyDictionary<int, TaskForecast> forecasts) =>
        dependencies
            .Select(task => forecasts.TryGetValue(task.Id, out var forecast) && forecast.Segments.Count > 0
                ? (End: forecast.Segments[^1].End, TaskId: (int?)task.Id)
                : (End: DateTime.MinValue, TaskId: (int?)null))
            .OrderByDescending(item => item.End)
            .FirstOrDefault();

    private MemberCursor CriarCursor(ForecastInput input, ForecastMember member)
    {
        var cursor = new MemberCursor(input.ForecastDate, member.DailyCapacityHours);
        MoverPara(cursor, input.Now ?? DateTime.MinValue);
        return cursor;
    }

    private void MoverPara(MemberCursor cursor, DateTime instant)
    {
        if (instant <= ObterInstante(cursor)) return;
        var time = TimeOnly.FromDateTime(instant);
        var elapsed = time <= workday.WorkdayStart ? 0m
            : time >= workday.WorkdayEnd ? ObterDuracaoDoExpediente()
            : (decimal)(time - workday.WorkdayStart).TotalHours;
        cursor.Date = DateOnly.FromDateTime(instant);
        cursor.RemainingHours = Math.Round(cursor.DailyCapacity * (1 - elapsed / ObterDuracaoDoExpediente()), 4);
        if (cursor.RemainingHours > 0) return;
        cursor.Date = cursor.Date.AddDays(1);
        cursor.RemainingHours = cursor.DailyCapacity;
    }

    private DateTime ObterInstante(MemberCursor cursor) =>
        ObterInstante(cursor.Date, cursor.DailyCapacity - cursor.RemainingHours, cursor.DailyCapacity);

    private DateTime ObterInstante(DateOnly date, decimal usedHours, decimal capacity) =>
        date.ToDateTime(workday.WorkdayStart).AddMinutes((double)Math.Round(usedHours / capacity * ObterDuracaoDoExpediente() * 60));

    private static ForecastOptions ValidarExpediente(ForecastOptions value) => value.WorkdayEnd > value.WorkdayStart
        ? value
        : throw new ArgumentException("O fim do expediente deve ser posterior ao início.", nameof(value));

    private decimal ObterDuracaoDoExpediente() => (decimal)(workday.WorkdayEnd - workday.WorkdayStart).TotalHours;

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

    private static bool EstaEmAndamento(ForecastTask task) =>
        task.StartedAt is not null && string.Equals(task.State, "In Progress", StringComparison.OrdinalIgnoreCase);

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
