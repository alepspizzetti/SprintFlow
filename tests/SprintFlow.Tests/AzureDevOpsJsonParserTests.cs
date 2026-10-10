using SprintFlow.AzureDevOps;

namespace SprintFlow.Tests;

public sealed class AzureDevOpsJsonParserTests
{
    [Fact]
    public void Reads_wrapped_capacity_response()
    {
        const string json = """
        {"value":[{"teamMember":{"id":"u1","displayName":"Ana"},"activities":[{"name":"Backend","capacityPerDay":6}],"daysOff":[]}]}
        """;

        var capacity = Assert.Single(AzureDevOpsJsonParser.ReadList<AzureCapacity>(json));

        Assert.Equal("u1", capacity.TeamMember.Id);
        Assert.Equal(6, Assert.Single(capacity.Activities).CapacityPerDay);
    }

    [Fact]
    public void Reads_work_item_fields_and_falls_back_to_original_estimate()
    {
        const string json = """
        {"fields":{"System.Id":42,"System.Parent":10,"System.WorkItemType":"Task","System.Title":"Criar API","System.State":"Active","System.AssignedTo":{"id":"u1","displayName":"Ana"},"Microsoft.VSTS.Scheduling.OriginalEstimate":12,"Microsoft.VSTS.Common.StackRank":3}}
        """;

        var item = AzureDevOpsJsonParser.ReadWorkItem(json);

        Assert.Equal(42, item.Id);
        Assert.Equal(10, item.ParentId);
        Assert.Equal("u1", item.AssignedToId);
        Assert.Equal(12, item.RemainingWork);
        Assert.Equal(3, item.Order);
    }

    [Fact]
    public void Builds_assigned_work_pause_resume_and_completion_from_revisions()
    {
        const string json = """
        {"value":[
          {"fields":{"System.State":"To Do","System.ChangedDate":"2026-10-05T09:00:00Z","System.AssignedTo":{"id":"ana","displayName":"Ana"}}},
          {"fields":{"System.State":"In Progress","System.ChangedDate":"2026-10-06T10:00:00Z","System.AssignedTo":{"id":"ana","displayName":"Ana"}}},
          {"fields":{"System.State":"In Progress","System.ChangedDate":"2026-10-06T11:00:00Z","System.AssignedTo":{"id":"ana","displayName":"Ana"}}},
          {"fields":{"System.State":"To Do","System.ChangedDate":"2026-10-07T12:00:00Z","System.AssignedTo":{"id":"ana","displayName":"Ana"}}},
          {"fields":{"System.State":"In Progress","System.ChangedDate":"2026-10-08T14:00:00Z","System.AssignedTo":{"id":"bia","displayName":"Bia"}}},
          {"fields":{"System.State":"Done","System.ChangedDate":"2026-10-09T16:00:00Z","System.AssignedTo":{"id":"bia","displayName":"Bia"}}}
        ]}
        """;

        var periods = AzureDevOpsSprintPlanningSource.BuildActivityPeriods(AzureDevOpsJsonParser.ReadRevisions(json));

        Assert.Equal(["Assigned", "Active", "Paused", "Active", "Done"], periods.Select(item => item.Kind));
        Assert.Equal("ana", periods[1].MemberId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-07T12:00:00Z"), periods[1].EndedAt);
        Assert.Equal("bia", periods[3].MemberId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T16:00:00Z"), periods[3].EndedAt);
    }
}
