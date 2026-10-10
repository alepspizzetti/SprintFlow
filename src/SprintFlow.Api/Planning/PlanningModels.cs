namespace SprintFlow.Planning;

public enum SquadRole { Backend, Frontend, QA, PD, EL, GPM, PM }
public enum WorkStage { Backend, Frontend, QA, Other }

public sealed record PlanningMember(
    string Id,
    string Name,
    SquadRole Role,
    decimal DailyCapacityHours,
    HashSet<DateOnly>? DaysOff = null);

public sealed record PlanningStory(int Id, string Title, decimal Order);

public sealed record PlanningTask(
    int Id,
    int StoryId,
    string Title,
    string? AssignedMemberId,
    WorkStage Stage,
    decimal RemainingWork,
    string State,
    decimal? Order = null);

public sealed record PlanningInput(
    DateOnly SprintStart,
    DateOnly SprintEnd,
    DateOnly PlanningDate,
    IReadOnlyList<PlanningMember> Members,
    IReadOnlyList<PlanningStory> Stories,
    IReadOnlyList<PlanningTask> Tasks);

public sealed record AllocationSegment(DateOnly Date, decimal Hours);

public sealed record PlannedTask(
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

public sealed record PlanningResult(
    IReadOnlyList<PlannedTask> Tasks,
    IReadOnlyList<FlowBlockage> Blockages,
    IReadOnlyList<int> IgnoredTaskIds);
