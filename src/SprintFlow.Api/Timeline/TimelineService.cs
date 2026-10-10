using SprintFlow.Planning;

namespace SprintFlow.Timeline;

public sealed class TimelineService(ISprintPlanningSource source, ObjectivePlanningEngine engine)
{
    public Task<IReadOnlyList<SprintSummary>> ListSprintsAsync(CancellationToken cancellationToken = default) => source.ListAsync(cancellationToken);

    public async Task<TimelineResponse?> SyncAsync(string sprintId, CancellationToken cancellationToken = default)
    {
        if (source is ISprintPlanningRefresher refresher && !await refresher.RefreshAsync(sprintId, cancellationToken)) return null;
        return await GetAsync(sprintId, cancellationToken);
    }

    public async Task<TimelineResponse?> GetAsync(string sprintId, CancellationToken cancellationToken = default)
    {
        var dataset = await source.GetAsync(sprintId, cancellationToken);
        if (dataset is null) return null;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var sprint = dataset.Sprint;
        var planningDate = sprint.Status == "Current"
            ? today < sprint.StartDate ? sprint.StartDate : today > sprint.EndDate ? sprint.EndDate : today
            : sprint.StartDate;
        var input = dataset.Input with { PlanningDate = planningDate };
        var plan = engine.Build(input);
        var tasksById = input.Tasks.ToDictionary(task => task.Id);
        var storiesById = input.Stories.ToDictionary(story => story.Id);
        var people = input.Members.Select(member =>
        {
            var tasks = plan.Tasks.Where(task => task.AssignedMemberId == member.Id).Select(task =>
            {
                var original = tasksById[task.Id];
                var segments = task.Segments.Select(segment => new TimelineSegment(segment.Date, segment.Hours)).ToList();
                return new TimelineTask(task.Id, task.StoryId, storiesById[task.StoryId].Title, task.Title,
                    task.Stage, task.State, original.RemainingWork, original.Order, segments, task.ExceedsSprint,
                    dataset.WorkItemUrls?.GetValueOrDefault(task.Id), dataset.Activity?.GetValueOrDefault(task.Id) ?? []);
            }).ToList();
            tasks.AddRange(input.Tasks.Where(task => task.AssignedMemberId == member.Id && plan.IgnoredTaskIds.Contains(task.Id))
                .Select(task => new TimelineTask(task.Id, task.StoryId, storiesById[task.StoryId].Title, task.Title,
                    task.Stage, task.State, task.RemainingWork, task.Order, [], false,
                    dataset.WorkItemUrls?.GetValueOrDefault(task.Id), dataset.Activity?.GetValueOrDefault(task.Id) ?? [])));
            return new TimelinePerson(member.Id, member.Name, member.Role, member.DailyCapacityHours, tasks,
                plan.Blockages.Where(item => item.AffectedMemberId == member.Id)
                    .Select(item => new TimelineBlockage(item.AffectedTaskId, item.BlockingTaskId, item.Date, item.BlockedHours, item.Cause)).ToList());
        }).ToList();
        var memberIds = input.Members.Select(member => member.Id).ToHashSet();
        var withoutMember = input.Tasks.Where(task => task.AssignedMemberId is null || !memberIds.Contains(task.AssignedMemberId)).ToList();
        if (withoutMember.Count > 0)
            people.Add(new TimelinePerson("", "Sem responsável na squad", SquadRole.PD, 0,
                withoutMember.Select(task => new TimelineTask(task.Id, task.StoryId, storiesById[task.StoryId].Title,
                    task.Title, task.Stage, task.State, task.RemainingWork, task.Order, [], false,
                    dataset.WorkItemUrls?.GetValueOrDefault(task.Id), dataset.Activity?.GetValueOrDefault(task.Id) ?? [])).ToList(), []));

        var stories = input.Stories.OrderBy(story => story.Order).ThenBy(story => story.Id)
            .Select(story => new TimelineStory(story.Id, story.Title, story.Order,
                input.Tasks.Count(task => task.StoryId == story.Id && task.State == "Done"),
                input.Tasks.Count(task => task.StoryId == story.Id))).ToList();
        var ignoredStoryIds = input.Tasks.Where(task => plan.IgnoredTaskIds.Contains(task.Id)).Select(task => task.StoryId).ToHashSet();
        var delivered = stories.Count(story => story.TotalTasks > 0 && !ignoredStoryIds.Contains(story.Id) &&
            plan.Tasks.Where(task => task.StoryId == story.Id).All(task => !task.ExceedsSprint));
        var forecastEnd = plan.Tasks.Select(task => task.EndDate).OfType<DateOnly>().Cast<DateOnly?>().Max();
        return new TimelineResponse(sprint, planningDate, WorkingDays(sprint.StartDate, sprint.EndDate), stories, people,
            new TimelineSummary(delivered, stories.Count, forecastEnd), plan.IgnoredTaskIds.Count, dataset.SyncedAt);
    }

    private static IReadOnlyList<DateOnly> WorkingDays(DateOnly start, DateOnly end)
    {
        var dates = new List<DateOnly>();
        for (var date = start; date <= end; date = date.AddDays(1))
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) dates.Add(date);
        return dates;
    }
}
