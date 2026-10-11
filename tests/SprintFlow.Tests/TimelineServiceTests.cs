using SprintFlow.Forecast;
using SprintFlow.Timeline;

namespace SprintFlow.Tests;

public sealed class TimelineServiceTests
{
    [Fact]
    public async Task SeparaFolgasColetivasDasIndividuaisEConsideraAmbasNaPrevisao()
    {
        var start = new DateOnly(2026, 8, 24);
        var input = new ForecastInput(start, start.AddDays(4), start,
            [new ForecastMember("ana", "Ana", SquadRole.Backend, 8, [start.AddDays(1)]),
             new ForecastMember("bia", "Bia", SquadRole.Backend, 8)], [new ForecastStory(100, "US", 1)],
            [new ForecastTask(1, 100, "API A", "ana", WorkStage.Backend, 8, "Todo"),
             new ForecastTask(2, 100, "API B", "bia", WorkStage.Backend, 8, "Todo")]);
        var dataset = new SprintForecastDataset(new SprintSummary("sprint", "Sprint", start, input.SprintEnd, "Future"), input,
            TeamDaysOff: [start]);

        var response = await new TimelineService(new Source(dataset), new ForecastEngine()).ObterAsync("sprint");

        Assert.NotNull(response);
        Assert.Equal(new[] { start }, response.TeamDaysOff);
        var ana = Assert.Single(response.People, person => person.Id == "ana");
        var bia = Assert.Single(response.People, person => person.Id == "bia");
        Assert.Equal(new[] { start.AddDays(1) }, ana.DaysOff);
        Assert.Empty(bia.DaysOff);
        Assert.Equal(start.AddDays(2), Assert.Single(Assert.Single(ana.Tasks).Segments).Date);
        Assert.Equal(start.AddDays(1), Assert.Single(Assert.Single(bia.Tasks).Segments).Date);
        Assert.DoesNotContain(start, input.Members[0].DaysOff!);
    }

    [Fact]
    public async Task IncluiFolgasDePessoasSemTarefasEListaVaziaQuandoNaoHaFolgas()
    {
        var start = new DateOnly(2026, 8, 24);
        var input = new ForecastInput(start, start.AddDays(11), start,
            [new ForecastMember("ana", "Ana", SquadRole.Backend, 8, [start.AddDays(2), start]),
             new ForecastMember("bia", "Bia", SquadRole.Frontend, 8)], [new ForecastStory(100, "US", 1)],
            [new ForecastTask(1, 100, "Sem responsável", null, WorkStage.Backend, 0, "Todo")]);
        var dataset = new SprintForecastDataset(new SprintSummary("sprint", "Sprint", start, input.SprintEnd, "Future"), input);
        var service = new TimelineService(new Source(dataset), new ForecastEngine());

        var response = await service.ObterAsync("sprint");

        Assert.NotNull(response);
        var ana = Assert.Single(response.People, person => person.Id == "ana");
        Assert.Empty(response.TeamDaysOff);
        Assert.Equal(new[] { start, start.AddDays(2) }, ana.DaysOff);
        Assert.Empty(ana.Tasks);
        Assert.Empty(Assert.Single(response.People, person => person.Id == "bia").DaysOff);
        Assert.Empty(Assert.Single(response.People, person => person.Id == "").DaysOff);
    }

    [Theory]
    [InlineData("Done", null, true)]
    [InlineData("Todo", null, false)]
    [InlineData("Unknown", null, false)]
    [InlineData("Done", "Review", true)]
    [InlineData("Todo", "Done", false)]
    [InlineData("Testing", "Done", false)]
    [InlineData("Awaiting Release", "Done", false)]
    public async Task Conta_historias_concluidas_pelo_estado_da_historia_independentemente_das_tarefas(
        string storyState, string? taskState, bool expected)
    {
        var start = new DateOnly(2026, 8, 24);
        var tasks = taskState is null ? Array.Empty<ForecastTask>() :
            [new ForecastTask(1, 100, "Task", null, WorkStage.Backend, 0, taskState)];
        var input = new ForecastInput(start, start.AddDays(11), start, [],
            [new ForecastStory(100, "US", 1, storyState)], tasks);
        var dataset = new SprintForecastDataset(new SprintSummary("sprint", "Sprint", start, input.SprintEnd, "Future"), input);
        var service = new TimelineService(new Source(dataset), new ForecastEngine());

        var response = await service.ObterAsync("sprint");

        Assert.NotNull(response);
        Assert.Equal(expected, Assert.Single(response.Stories).IsDone);
        Assert.Equal(storyState, Assert.Single(response.Stories).State);
        Assert.Equal(expected ? 1 : 0, response.Summary.CompletedStories);
        Assert.Equal(1, response.Summary.TotalStories);
    }

    [Theory]
    [InlineData("ana", 8, true, false)]
    [InlineData("ana", 120, true, true)]
    [InlineData("ana", 0, false, false)]
    [InlineData(null, 8, false, false)]
    [InlineData("fora-da-squad", 8, false, false)]
    public async Task Preserva_dados_da_tarefa_com_e_sem_previsao_e_responsavel(
        string? memberId, int remainingWork, bool hasForecast, bool exceedsSprint)
    {
        var start = new DateOnly(2026, 8, 24);
        var task = new ForecastTask(1, 100, "Criar API", memberId, WorkStage.Backend, remainingWork, "In Progress", 2);
        var input = new ForecastInput(start, start.AddDays(11), start,
            [new ForecastMember("ana", "Ana", SquadRole.Backend, 8)],
            [new ForecastStory(100, "Minha US", 1, Description: "<p>Descrição completa</p>")], [task]);
        var activity = new TaskActivityPeriod(memberId, "Ana", "Active", new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.Zero), null);
        var dataset = new SprintForecastDataset(new SprintSummary("sprint", "Sprint", start, input.SprintEnd, "Future"), input,
            new Dictionary<int, string> { [1] = "https://example.com/workitems/1", [100] = "https://example.com/workitems/100" },
            new Dictionary<int, IReadOnlyList<TaskActivityPeriod>> { [1] = [activity] });
        var service = new TimelineService(new Source(dataset), new ForecastEngine());

        var response = await service.ObterAsync("sprint");

        Assert.NotNull(response);
        var story = Assert.Single(response.Stories);
        Assert.Equal("<p>Descrição completa</p>", story.Description);
        Assert.Equal("https://example.com/workitems/100", story.AzureUrl);
        var owner = Assert.Single(response.People, person => person.Tasks.Count > 0);
        var result = Assert.Single(owner.Tasks);
        Assert.Equal(memberId == "ana" ? "ana" : "", owner.Id);
        Assert.Equal(task.Id, result.Id);
        Assert.Equal(task.StoryId, result.StoryId);
        Assert.Equal("Minha US", result.StoryTitle);
        Assert.Equal(task.Title, result.Title);
        Assert.Equal(task.Stage, result.Stage);
        Assert.Equal(task.State, result.State);
        Assert.Equal(task.RemainingWork, result.RemainingWork);
        Assert.Equal(task.Order, result.Order);
        Assert.Equal("https://example.com/workitems/1", result.AzureUrl);
        Assert.Equal(activity, Assert.Single(result.Activity));
        Assert.Equal(hasForecast, result.Segments.Count > 0);
        Assert.Equal(hasForecast ? remainingWork : 0, result.Segments.Sum(segment => segment.Hours));
        Assert.Equal(exceedsSprint, result.ExceedsSprint);
    }

    private sealed class Source(SprintForecastDataset dataset) : ISprintForecastSource
    {
        public Task<IReadOnlyList<SprintSummary>> ListarAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SprintSummary>>([dataset.Sprint]);

        public Task<SprintForecastDataset?> ObterAsync(string sprintId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SprintForecastDataset?>(dataset);
    }
}
