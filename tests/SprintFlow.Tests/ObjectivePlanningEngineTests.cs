using SprintFlow.Planning;

namespace SprintFlow.Tests;

public sealed class ObjectivePlanningEngineTests
{
    private static readonly DateOnly Monday = new(2026, 8, 24);

    [Fact]
    public void Distributes_a_task_across_the_members_daily_capacity()
    {
        var result = Build(
            [Member("back", SquadRole.Backend, 8)],
            [Task(1, "back", WorkStage.Backend, 12)]);

        var task = Assert.Single(result.Tasks);
        Assert.Collection(task.Segments,
            first => { Assert.Equal(Monday, first.Date); Assert.Equal(8, first.Hours); },
            second => { Assert.Equal(Monday.AddDays(1), second.Date); Assert.Equal(4, second.Hours); });
    }

    [Fact]
    public void Skips_weekends_and_days_off()
    {
        var friday = new DateOnly(2026, 8, 28);
        var member = new PlanningMember("back", "Backend", SquadRole.Backend, 8,
            new HashSet<DateOnly> { friday.AddDays(3) });
        var input = Input(friday, [member], [Task(1, "back", WorkStage.Backend, 16)]);

        var task = Assert.Single(new ObjectivePlanningEngine().Build(input).Tasks);

        Assert.Equal([friday, friday.AddDays(4)], task.Segments.Select(item => item.Date));
    }

    [Fact]
    public void Creates_frontend_blockage_until_backend_is_available()
    {
        var result = Build(
            [Member("back", SquadRole.Backend, 8), Member("front", SquadRole.Frontend, 8)],
            [Task(1, "back", WorkStage.Backend, 16), Task(2, "front", WorkStage.Frontend, 8)]);

        var blockage = result.Blockages.First();
        Assert.Equal(2, result.Blockages.Count);
        Assert.Equal("front", blockage.AffectedMemberId);
        Assert.Equal(2, blockage.AffectedTaskId);
        Assert.Equal(1, blockage.BlockingTaskId);
        Assert.Equal(Monday, blockage.Date);
    }

    [Fact]
    public void Qa_waits_for_backend_and_frontend_from_the_same_story()
    {
        var result = Build(
            [Member("back", SquadRole.Backend, 8), Member("front", SquadRole.Frontend, 8), Member("qa", SquadRole.QA, 8)],
            [Task(1, "back", WorkStage.Backend, 8), Task(2, "front", WorkStage.Frontend, 8), Task(3, "qa", WorkStage.QA, 8)]);

        var frontend = result.Tasks.Single(item => item.Id == 2);
        var qa = result.Tasks.Single(item => item.Id == 3);
        Assert.True(qa.StartDate >= frontend.EndDate);
        Assert.Contains(result.Blockages, item => item.AffectedTaskId == 3);
    }

    [Fact]
    public void Ignores_unassigned_tasks()
    {
        var result = Build([Member("back", SquadRole.Backend, 8)],
            [new PlanningTask(1, 100, "Sem responsável", null, WorkStage.Backend, 8, "Todo")]);

        Assert.Empty(result.Tasks);
        Assert.Equal([1], result.IgnoredTaskIds);
    }

    [Fact]
    public void Ignores_tasks_without_daily_capacity()
    {
        var result = Build([Member("back", SquadRole.Backend, 0)],
            [Task(1, "back", WorkStage.Backend, 8)]);

        Assert.Empty(result.Tasks);
        Assert.Equal([1], result.IgnoredTaskIds);
    }

    [Fact]
    public void Follows_the_task_order_from_azure_within_a_story()
    {
        var result = Build([Member("back", SquadRole.Backend, 8)],
            [new PlanningTask(1, 100, "Depois", "back", WorkStage.Backend, 8, "Todo", 2),
             new PlanningTask(2, 100, "Antes", "back", WorkStage.Backend, 8, "Todo", 1)]);

        Assert.Equal(Monday, result.Tasks.Single(task => task.Id == 2).StartDate);
        Assert.Equal(Monday.AddDays(1), result.Tasks.Single(task => task.Id == 1).StartDate);
    }

    private static PlanningResult Build(IReadOnlyList<PlanningMember> members, IReadOnlyList<PlanningTask> tasks) =>
        new ObjectivePlanningEngine().Build(Input(Monday, members, tasks));

    private static PlanningInput Input(DateOnly start, IReadOnlyList<PlanningMember> members, IReadOnlyList<PlanningTask> tasks) =>
        new(start, start.AddDays(11), start, members, [new PlanningStory(100, "US", 1)], tasks);

    private static PlanningMember Member(string id, SquadRole role, decimal capacity) =>
        new(id, id, role, capacity);

    private static PlanningTask Task(int id, string member, WorkStage stage, decimal hours) =>
        new(id, 100, $"Task {id}", member, stage, hours, "Todo");
}
