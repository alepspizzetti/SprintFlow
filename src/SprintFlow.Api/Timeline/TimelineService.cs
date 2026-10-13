using SprintFlow.Forecast;

namespace SprintFlow.Timeline;

public sealed class TimelineService(ISprintForecastSource source, ForecastEngine engine)
{
    public Task<IReadOnlyList<SprintSummary>> ListarSprintsAsync(CancellationToken cancellationToken = default) => source.ListarAsync(cancellationToken);

    public async Task<TimelineResponse?> SincronizarAsync(string sprintId, CancellationToken cancellationToken = default)
    {
        if (source is ISprintForecastRefresher refresher && !await refresher.AtualizarAsync(sprintId, cancellationToken)) return null;
        return await ObterAsync(sprintId, cancellationToken);
    }

    public async Task<TimelineResponse?> ObterAsync(string sprintId, CancellationToken cancellationToken = default)
    {
        var dataset = await source.ObterAsync(sprintId, cancellationToken);
        if (dataset is null) return null;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var sprint = dataset.Sprint;
        var forecastDate = sprint.Status == "Current"
            ? today < sprint.StartDate ? sprint.StartDate : today > sprint.EndDate ? sprint.EndDate : today
            : sprint.StartDate;
        var input = dataset.Input with {
            ForecastDate = forecastDate,
            Now = forecastDate == today ? now : null,
            Tasks = dataset.Input.Tasks.Select(task => task with {
                StartedAt = dataset.Activity?.GetValueOrDefault(task.Id)?.FirstOrDefault(period => period.Kind == "Active")?.StartedAt
            }).ToList(),
            Members = dataset.Input.Members.Select(member => member with {
                DaysOff = (member.DaysOff ?? []).Concat(dataset.TeamDaysOff ?? []).ToHashSet()
            }).ToList()
        };
        var forecast = engine.CalcularPrevisao(input);
        var tasksById = input.Tasks.ToDictionary(task => task.Id);
        var storiesById = input.Stories.ToDictionary(story => story.Id);
        var people = dataset.Input.Members.Select(member =>
        {
            var tasks = forecast.Tasks.Where(task => task.AssignedMemberId == member.Id)
                .Select(task => MontarTarefaDaTimeline(tasksById[task.Id], storiesById[task.StoryId].Title, dataset, task)).ToList();
            tasks.AddRange(input.Tasks.Where(task => task.AssignedMemberId == member.Id && forecast.IgnoredTaskIds.Contains(task.Id))
                .Select(task => MontarTarefaDaTimeline(task, storiesById[task.StoryId].Title, dataset)));
            return new TimelinePerson(member.Id, member.Name, member.Role, member.DailyCapacityHours, tasks,
                forecast.Blockages.Where(item => item.AffectedMemberId == member.Id)
                    .Select(item => new TimelineBlockage(item.AffectedTaskId, item.BlockingTaskId, item.Date, item.BlockedHours, item.Cause)).ToList(),
                member.DaysOff?.OrderBy(date => date).ToList() ?? []);
        }).ToList();
        var memberIds = input.Members.Select(member => member.Id).ToHashSet();
        var withoutMember = input.Tasks.Where(task => task.AssignedMemberId is null || !memberIds.Contains(task.AssignedMemberId)).ToList();
        if (withoutMember.Count > 0)
            people.Add(new TimelinePerson("", "Sem responsável na squad", SquadRole.PD, 0,
                withoutMember.Select(task => MontarTarefaDaTimeline(task, storiesById[task.StoryId].Title, dataset)).ToList(), [], []));

        var stories = input.Stories.OrderBy(story => story.Order).ThenBy(story => story.Id)
            .Select(story =>
            {
                var tasks = input.Tasks.Where(task => task.StoryId == story.Id).ToList();
                var completedTasks = tasks.Count(task => task.State == "Done");
                var isDone = story.State == "Done";
                return new TimelineStory(story.Id, story.Title, story.Order, completedTasks, tasks.Count, isDone, story.State, story.Description, dataset.WorkItemUrls?.GetValueOrDefault(story.Id));
            }).ToList();
        var completedStories = stories.Count(story => story.IsDone);
        var forecastEnd = forecast.Tasks.Select(task => task.EndDate).OfType<DateOnly>().Cast<DateOnly?>().Max();
        return new TimelineResponse(sprint, forecastDate, ObterDiasUteis(sprint.StartDate, sprint.EndDate), stories, people,
            new TimelineSummary(completedStories, stories.Count, forecastEnd), forecast.IgnoredTaskIds.Count, dataset.SyncedAt,
            dataset.TeamDaysOff ?? []);
    }

    private static TimelineTask MontarTarefaDaTimeline(ForecastTask task, string storyTitle,
        SprintForecastDataset dataset, TaskForecast? forecast = null) =>
        new(task.Id, task.StoryId, storyTitle, task.Title, task.Stage, task.State, task.RemainingWork, task.Order,
            forecast?.Segments.Select(segment => new TimelineSegment(segment.Date, segment.Hours, segment.Start, segment.End)).ToList() ?? [],
            forecast?.ExceedsSprint ?? false, dataset.WorkItemUrls?.GetValueOrDefault(task.Id),
            dataset.Activity?.GetValueOrDefault(task.Id) ?? []);

    private static IReadOnlyList<DateOnly> ObterDiasUteis(DateOnly start, DateOnly end)
    {
        var dates = new List<DateOnly>();
        for (var date = start; date <= end; date = date.AddDays(1))
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) dates.Add(date);
        return dates;
    }
}
