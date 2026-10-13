using System.Text.Json;
using SprintFlow.Forecast;

namespace SprintFlow.Tests;

public sealed class ForecastEngineTests
{
    private static readonly DateOnly Monday = new(2026, 8, 24);

    [Fact]
    public void Preserva_data_da_previsao_do_cache_existente()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var input = JsonSerializer.Deserialize<ForecastInput>(
            """{"sprintStart":"2026-08-24","sprintEnd":"2026-09-04","planningDate":"2026-08-25","members":[],"stories":[],"tasks":[]}""", options)!;

        Assert.Equal(Monday.AddDays(1), input.ForecastDate);
        Assert.Contains("\"planningDate\":\"2026-08-25\"", JsonSerializer.Serialize(input, options));
    }

    [Fact]
    public void Distribui_tarefa_pela_capacidade_diaria_do_membro()
    {
        var result = CalcularPrevisao(
            [CriarMembro("back", SquadRole.Backend, 8)],
            [CriarTarefa(1, "back", WorkStage.Backend, 12)]);

        var task = Assert.Single(result.Tasks);
        Assert.Collection(task.Segments,
            first => { Assert.Equal(Monday, first.Date); Assert.Equal(8, first.Hours); },
            second => { Assert.Equal(Monday.AddDays(1), second.Date); Assert.Equal(4, second.Hours); });
    }

    [Fact]
    public void Ignora_fins_de_semana_e_folgas()
    {
        var friday = new DateOnly(2026, 8, 28);
        var member = new ForecastMember("back", "Backend", SquadRole.Backend, 8,
            new HashSet<DateOnly> { friday.AddDays(3) });
        var input = CriarEntrada(friday, [member], [CriarTarefa(1, "back", WorkStage.Backend, 16)]);

        var task = Assert.Single(new ForecastEngine().CalcularPrevisao(input).Tasks);

        Assert.Equal([friday, friday.AddDays(4)], task.Segments.Select(item => item.Date));
    }

    [Fact]
    public void Cria_bloqueio_do_frontend_ate_backend_estar_disponivel()
    {
        var result = CalcularPrevisao(
            [CriarMembro("back", SquadRole.Backend, 8), CriarMembro("front", SquadRole.Frontend, 8)],
            [CriarTarefa(1, "back", WorkStage.Backend, 16), CriarTarefa(2, "front", WorkStage.Frontend, 8)]);

        var blockage = result.Blockages.First();
        Assert.Equal(2, result.Blockages.Count);
        Assert.Equal("front", blockage.AffectedMemberId);
        Assert.Equal(2, blockage.AffectedTaskId);
        Assert.Equal(1, blockage.BlockingTaskId);
        Assert.Equal(Monday, blockage.Date);
    }

    [Fact]
    public void Qa_aguarda_backend_e_frontend_da_mesma_historia()
    {
        var result = CalcularPrevisao(
            [CriarMembro("back", SquadRole.Backend, 8), CriarMembro("front", SquadRole.Frontend, 8), CriarMembro("qa", SquadRole.QA, 8)],
            [CriarTarefa(1, "back", WorkStage.Backend, 8), CriarTarefa(2, "front", WorkStage.Frontend, 8), CriarTarefa(3, "qa", WorkStage.QA, 8)]);

        var frontend = result.Tasks.Single(item => item.Id == 2);
        var qa = result.Tasks.Single(item => item.Id == 3);
        Assert.True(qa.StartDate >= frontend.EndDate);
        Assert.Contains(result.Blockages, item => item.AffectedTaskId == 3);
    }

    [Fact]
    public void Ignora_tarefas_sem_responsavel()
    {
        var result = CalcularPrevisao([CriarMembro("back", SquadRole.Backend, 8)],
            [new ForecastTask(1, 100, "Sem responsável", null, WorkStage.Backend, 8, "Todo")]);

        Assert.Empty(result.Tasks);
        Assert.Equal([1], result.IgnoredTaskIds);
    }

    [Fact]
    public void Ignora_tarefas_sem_capacidade_diaria()
    {
        var result = CalcularPrevisao([CriarMembro("back", SquadRole.Backend, 0)],
            [CriarTarefa(1, "back", WorkStage.Backend, 8)]);

        Assert.Empty(result.Tasks);
        Assert.Equal([1], result.IgnoredTaskIds);
    }

    [Fact]
    public void Respeita_ordem_das_tarefas_do_azure_dentro_da_historia()
    {
        var result = CalcularPrevisao([CriarMembro("back", SquadRole.Backend, 8)],
            [new ForecastTask(1, 100, "Depois", "back", WorkStage.Backend, 8, "Todo", 2),
             new ForecastTask(2, 100, "Antes", "back", WorkStage.Backend, 8, "Todo", 1)]);

        Assert.Equal(Monday, result.Tasks.Single(task => task.Id == 2).StartDate);
        Assert.Equal(Monday.AddDays(1), result.Tasks.Single(task => task.Id == 1).StartDate);
    }

    [Fact]
    public void Enfileira_tarefas_da_mesma_pessoa_sem_sobreposicao()
    {
        var result = CalcularPrevisao([CriarMembro("back", SquadRole.Backend, 8)],
            [CriarTarefa(1, "back", WorkStage.Backend, 2), CriarTarefa(2, "back", WorkStage.Backend, 2)]);

        var first = Assert.Single(result.Tasks.Single(task => task.Id == 1).Segments);
        var second = Assert.Single(result.Tasks.Single(task => task.Id == 2).Segments);
        Assert.Equal(Hora(8), first.Start);
        Assert.Equal(Hora(10, 30), first.End);
        Assert.Equal(first.End, second.Start);
    }

    [Fact]
    public void Tarefa_a_fazer_inicia_no_maior_entre_cadastro_agora_e_fim_da_anterior()
    {
        var input = CriarEntrada(Monday, [CriarMembro("back", SquadRole.Backend, 8)],
            [CriarTarefa(1, "back", WorkStage.Backend, 2) with { CreatedAt = new DateTimeOffset(Hora(9)) },
             CriarTarefa(2, "back", WorkStage.Backend, 2) with { CreatedAt = new DateTimeOffset(Hora(16)) }]) with { Now = Hora(13) };

        var result = new ForecastEngine().CalcularPrevisao(input);

        Assert.Equal(Hora(13), result.Tasks.Single(task => task.Id == 1).Segments[0].Start);
        Assert.Equal(Hora(16), result.Tasks.Single(task => task.Id == 2).Segments[0].Start);
    }

    [Fact]
    public void Tarefa_em_andamento_ocupa_a_pessoa_antes_da_fila_independente_da_ordem()
    {
        var input = CriarEntrada(Monday, [CriarMembro("back", SquadRole.Backend, 8)],
            [new ForecastTask(1, 100, "Primeira", "back", WorkStage.Backend, 2, "Todo", 1),
             new ForecastTask(2, 100, "Segunda", "back", WorkStage.Backend, 2, "In Progress", 2, StartedAt: new DateTimeOffset(Hora(9)))]) with { Now = Hora(10) };

        var result = new ForecastEngine().CalcularPrevisao(input);

        var active = Assert.Single(result.Tasks.Single(task => task.Id == 2).Segments);
        Assert.Equal(Hora(10), active.Start);
        Assert.Equal(active.End, result.Tasks.Single(task => task.Id == 1).Segments[0].Start);
    }

    [Fact]
    public void Tarefas_em_andamento_da_mesma_pessoa_ficam_sobrepostas()
    {
        var startedAt = new DateTimeOffset(Hora(9));
        var input = CriarEntrada(Monday, [CriarMembro("back", SquadRole.Backend, 8)],
            [new ForecastTask(1, 100, "A", "back", WorkStage.Backend, 2, "In Progress", 1, StartedAt: startedAt),
             new ForecastTask(2, 100, "B", "back", WorkStage.Backend, 2, "In Progress", 2, StartedAt: startedAt)]) with { Now = Hora(10) };

        var result = new ForecastEngine().CalcularPrevisao(input);

        Assert.All(result.Tasks, task => Assert.Equal(Hora(10), task.Segments[0].Start));
    }

    private static DateTime Hora(int hour, int minute = 0) => Monday.ToDateTime(new TimeOnly(hour, minute));

    private static ForecastResult CalcularPrevisao(IReadOnlyList<ForecastMember> members, IReadOnlyList<ForecastTask> tasks) =>
        new ForecastEngine().CalcularPrevisao(CriarEntrada(Monday, members, tasks));

    private static ForecastInput CriarEntrada(DateOnly start, IReadOnlyList<ForecastMember> members, IReadOnlyList<ForecastTask> tasks) =>
        new(start, start.AddDays(11), start, members, [new ForecastStory(100, "US", 1)], tasks);

    private static ForecastMember CriarMembro(string id, SquadRole role, decimal capacity) =>
        new(id, id, role, capacity);

    private static ForecastTask CriarTarefa(int id, string member, WorkStage stage, decimal hours) =>
        new(id, 100, $"Task {id}", member, stage, hours, "Todo");
}
