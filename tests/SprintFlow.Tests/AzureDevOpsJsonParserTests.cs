using SprintFlow.AzureDevOps;

namespace SprintFlow.Tests;

public sealed class AzureDevOpsJsonParserTests
{
    [Theory]
    [InlineData("{\"daysOff\":[]}", 0)]
    [InlineData("{\"daysOff\":[{\"start\":\"2026-08-27T00:00:00Z\",\"end\":\"2026-08-28T00:00:00Z\"}]}", 1)]
    public void LeFolgasColetivasDaRespostaDoAzure(string json, int quantidade)
    {
        var folgas = AzureDevOpsJsonParser.LerLista<AzureDayOff>(json);

        Assert.Equal(quantidade, folgas.Count);
        if (quantidade > 0)
        {
            Assert.Equal(new DateOnly(2026, 8, 27), DateOnly.FromDateTime(folgas[0].Start.DateTime));
            Assert.Equal(new DateOnly(2026, 8, 28), DateOnly.FromDateTime(folgas[0].End.DateTime));
        }
    }

    [Fact]
    public void Le_itens_da_consulta_na_ordem_do_backlog_com_pai_e_esforco_restante()
    {
        const string json = """
        [
          {"fields":{"System.Id":100,"System.WorkItemType":"User Story","System.Title":"US","System.Description":"<p>Descrição completa</p>","System.State":"Done","Microsoft.VSTS.Common.StackRank":1}},
          {"fields":{"System.Id":42,"System.Parent":100,"System.WorkItemType":"Task","System.Title":"Criar API","System.State":"Active","System.AssignedTo":{"id":"u1"},"System.ChangedDate":"2026-10-09T16:00:00Z","Microsoft.VSTS.Scheduling.RemainingWork":4,"Microsoft.VSTS.Scheduling.OriginalEstimate":12,"Microsoft.VSTS.Common.BacklogPriority":2}}
        ]
        """;

        var items = AzureDevOpsJsonParser.LerItens(json);

        Assert.Equal([100, 42], items.Select(item => item.Id));
        Assert.Equal("Done", items[0].State);
        Assert.Equal("<p>Descrição completa</p>", items[0].Description);
        Assert.Null(items[1].Description);
        Assert.Equal(100, items[1].ParentId);
        Assert.Equal("u1", items[1].AssignedToId);
        Assert.Equal(4, items[1].RemainingWork);
        Assert.Equal(2, items[1].Order);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("null")]
    [InlineData("[]")]
    public void Le_resultados_vazios_da_consulta(string json)
    {
        Assert.Empty(AzureDevOpsJsonParser.LerItens(json));
    }

    [Fact]
    public void Le_resposta_de_capacidade_encapsulada()
    {
        const string json = """
        {"value":[{"teamMember":{"id":"u1","displayName":"Ana"},"activities":[{"name":"Backend","capacityPerDay":6}],"daysOff":[]}]}
        """;

        var capacity = Assert.Single(AzureDevOpsJsonParser.LerLista<AzureCapacity>(json));

        Assert.Equal("u1", capacity.TeamMember.Id);
        Assert.Equal(6, Assert.Single(capacity.Activities).CapacityPerDay);
    }

    [Fact]
    public void Le_campos_do_item_com_fallback_para_estimativa_original()
    {
        const string json = """
        [{"fields":{"System.Id":42,"System.Parent":10,"System.WorkItemType":"Task","System.Title":"Criar API","System.State":"Active","System.AssignedTo":{"id":"u1","displayName":"Ana"},"Microsoft.VSTS.Scheduling.OriginalEstimate":12,"Microsoft.VSTS.Common.StackRank":3}}]
        """;

        var item = Assert.Single(AzureDevOpsJsonParser.LerItens(json));

        Assert.Equal(42, item.Id);
        Assert.Equal(10, item.ParentId);
        Assert.Equal("u1", item.AssignedToId);
        Assert.Equal(12, item.RemainingWork);
        Assert.Equal(3, item.Order);
    }

    [Fact]
    public void Monta_atribuicao_trabalho_pausa_retomada_e_conclusao_das_revisoes()
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

        var periods = AzureDevOpsSprintForecastSource.MontarPeriodosDeAtividade(AzureDevOpsJsonParser.LerRevisoes(json));

        Assert.Equal(["Assigned", "Active", "Paused", "Active", "Done"], periods.Select(item => item.Kind));
        Assert.Equal("ana", periods[1].MemberId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-07T12:00:00Z"), periods[1].EndedAt);
        Assert.Equal("bia", periods[3].MemberId);
        Assert.Equal(DateTimeOffset.Parse("2026-10-09T16:00:00Z"), periods[3].EndedAt);
    }
}
