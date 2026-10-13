namespace SprintFlow.AzureDevOps;

public sealed record AzureIdentity(string Id, string DisplayName);
public sealed record AzureTeamMember(AzureIdentity Identity);
public sealed record AzureIteration(string Id, string Name, string? Path, AzureIterationAttributes? Attributes);
public sealed record AzureIterationAttributes(DateTimeOffset? StartDate, DateTimeOffset? FinishDate, string? TimeFrame);
public sealed record AzureCapacity(AzureIdentity TeamMember, IReadOnlyList<AzureCapacityActivity> Activities, IReadOnlyList<AzureDayOff> DaysOff);
public sealed record AzureCapacityActivity(string Name, decimal CapacityPerDay);
public sealed record AzureDayOff(DateTimeOffset Start, DateTimeOffset End);
public sealed record AzureWorkItem(int Id, int? ParentId, string Type, string Title, string State, string? AssignedToId, decimal RemainingWork, decimal? Order, string? Description = null, DateTimeOffset? CreatedAt = null);
public sealed record AzureWorkItemRevision(string State, DateTimeOffset ChangedAt, string? AssignedToId, string? AssignedToName);
public sealed class AzureDevOpsReadException(string message) : Exception(message);
