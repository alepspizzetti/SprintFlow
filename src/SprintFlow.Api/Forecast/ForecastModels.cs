using System.Text.Json.Serialization;

namespace SprintFlow.Forecast;

public enum SquadRole { Backend, Frontend, QA, PD, EL, GPM, PM }
public enum WorkStage { Backend, Frontend, QA, Other }

public sealed record ForecastMember(
    string Id,
    string Name,
    SquadRole Role,
    decimal DailyCapacityHours,
    HashSet<DateOnly>? DaysOff = null);

public sealed record ForecastStory(int Id, string Title, decimal Order, string State = "Unknown", string? Description = null);

public sealed record ForecastTask(
    int Id,
    int StoryId,
    string Title,
    string? AssignedMemberId,
    WorkStage Stage,
    decimal RemainingWork,
    string State,
    decimal? Order = null);

public sealed record ForecastInput(
    DateOnly SprintStart,
    DateOnly SprintEnd,
    [property: JsonPropertyName("planningDate")] DateOnly ForecastDate,
    IReadOnlyList<ForecastMember> Members,
    IReadOnlyList<ForecastStory> Stories,
    IReadOnlyList<ForecastTask> Tasks);

public sealed record AllocationSegment(DateOnly Date, decimal Hours);

public sealed record TaskForecast(
    int Id,
    int StoryId,
    string Title,
    string AssignedMemberId,
    WorkStage Stage,
    string State,
    IReadOnlyList<AllocationSegment> Segments,
    DateOnly? StartDate,
    DateOnly? EndDate,
    bool ExceedsSprint);

public sealed record FlowBlockage(
    string AffectedMemberId,
    int AffectedTaskId,
    int? BlockingTaskId,
    DateOnly Date,
    decimal BlockedHours,
    string Cause);

public sealed record ForecastResult(
    IReadOnlyList<TaskForecast> Tasks,
    IReadOnlyList<FlowBlockage> Blockages,
    IReadOnlyList<int> IgnoredTaskIds);
