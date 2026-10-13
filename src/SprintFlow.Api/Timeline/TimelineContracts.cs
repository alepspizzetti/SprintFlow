using SprintFlow.Forecast;

namespace SprintFlow.Timeline;

public sealed record SprintSummary(string Id, string Name, DateOnly StartDate, DateOnly EndDate, string Status);
public sealed record SprintForecastDataset(SprintSummary Sprint, ForecastInput Input,
    IReadOnlyDictionary<int, string>? WorkItemUrls = null,
    IReadOnlyDictionary<int, IReadOnlyList<TaskActivityPeriod>>? Activity = null,
    DateTimeOffset? SyncedAt = null, IReadOnlyList<DateOnly>? TeamDaysOff = null);

public interface ISprintForecastSource
{
    Task<IReadOnlyList<SprintSummary>> ListarAsync(CancellationToken cancellationToken = default);
    Task<SprintForecastDataset?> ObterAsync(string sprintId, CancellationToken cancellationToken = default);
}

public interface ISprintForecastRefresher
{
    Task<bool> AtualizarAsync(string sprintId, CancellationToken cancellationToken = default);
}

public sealed record TimelineResponse(SprintSummary Sprint, DateOnly Today, IReadOnlyList<DateOnly> Dates,
    IReadOnlyList<TimelineStory> Stories, IReadOnlyList<TimelinePerson> People, TimelineSummary Summary, int IgnoredTaskCount, DateTimeOffset? SyncedAt,
    IReadOnlyList<DateOnly> TeamDaysOff);
public sealed record TimelineStory(int Id, string Title, decimal Order, int CompletedTasks, int TotalTasks, bool IsDone = false, string State = "Todo", string? Description = null, string? AzureUrl = null);
public sealed record TimelinePerson(string Id, string Name, SquadRole Role, decimal DailyCapacityHours,
    IReadOnlyList<TimelineTask> Tasks, IReadOnlyList<TimelineBlockage> Blockages, IReadOnlyList<DateOnly> DaysOff);
public sealed record TimelineTask(int Id, int StoryId, string StoryTitle, string Title, WorkStage Stage, string State,
    decimal RemainingWork, decimal? Order, IReadOnlyList<TimelineSegment> Segments, bool ExceedsSprint, string? AzureUrl,
    IReadOnlyList<TaskActivityPeriod> Activity);
public sealed record TaskActivityPeriod(string? MemberId, string? MemberName, string Kind,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt);
public sealed record TimelineSegment(DateOnly Date, decimal Hours, DateTime Start, DateTime End);
public sealed record TimelineBlockage(int TaskId, int? BlockingTaskId, DateOnly Date, decimal Hours, string Cause);
public sealed record TimelineSummary(int CompletedStories, int TotalStories, DateOnly? ForecastEnd);
