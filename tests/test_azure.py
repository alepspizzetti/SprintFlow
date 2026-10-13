import unittest
from datetime import date, datetime
from decimal import Decimal

from sprintflow.azure_cli import ler_itens, ler_lista, ler_revisoes
from sprintflow.azure_source import montar_periodos_de_atividade


class AzureDevOpsJsonParserTests(unittest.TestCase):
    def test_le_folgas_coletivas_da_resposta_do_azure(self):
        self.assertEqual([], ler_lista('{"daysOff":[]}'))
        folgas = ler_lista('{"daysOff":[{"start":"2026-08-27T00:00:00Z","end":"2026-08-28T00:00:00Z"}]}')
        self.assertEqual(1, len(folgas))
        self.assertEqual(date(2026, 8, 27), datetime.fromisoformat(folgas[0]["start"]).date())
        self.assertEqual(date(2026, 8, 28), datetime.fromisoformat(folgas[0]["end"]).date())

    def test_le_itens_da_consulta_na_ordem_do_backlog_com_pai_e_esforco_restante(self):
        items = ler_itens("""
        [
          {"fields":{"System.Id":100,"System.WorkItemType":"User Story","System.Title":"US","System.Description":"<p>Descrição completa</p>","System.State":"Done","Microsoft.VSTS.Common.StackRank":1}},
          {"fields":{"System.Id":42,"System.Parent":100,"System.WorkItemType":"Task","System.Title":"Criar API","System.State":"Active","System.AssignedTo":{"id":"u1"},"System.ChangedDate":"2026-10-09T16:00:00Z","Microsoft.VSTS.Scheduling.RemainingWork":4,"Microsoft.VSTS.Scheduling.OriginalEstimate":12,"Microsoft.VSTS.Common.BacklogPriority":2}}
        ]
        """)

        self.assertEqual([100, 42], [item.id for item in items])
        self.assertEqual("Done", items[0].state)
        self.assertEqual("<p>Descrição completa</p>", items[0].description)
        self.assertIsNone(items[1].description)
        self.assertEqual((100, "u1", Decimal(4), Decimal(2)),
                         (items[1].parent_id, items[1].assigned_to_id, items[1].remaining_work, items[1].order))

    def test_le_resultados_vazios_da_consulta(self):
        for text in ("", "\n", "null", "[]"):
            with self.subTest(text=text):
                self.assertEqual([], ler_itens(text))

    def test_le_resposta_de_capacidade_encapsulada(self):
        capacity = ler_lista('{"value":[{"teamMember":{"id":"u1","displayName":"Ana"},"activities":[{"name":"Backend","capacityPerDay":6}],"daysOff":[]}]}')

        self.assertEqual(1, len(capacity))
        self.assertEqual("u1", capacity[0]["teamMember"]["id"])
        self.assertEqual(6, capacity[0]["activities"][0]["capacityPerDay"])

    def test_le_campos_do_item_com_fallback_para_estimativa_original(self):
        items = ler_itens('[{"fields":{"System.Id":42,"System.Parent":10,"System.WorkItemType":"Task","System.Title":"Criar API","System.State":"Active","System.AssignedTo":{"id":"u1","displayName":"Ana"},"System.CreatedDate":"2026-10-05T09:00:00Z","Microsoft.VSTS.Scheduling.OriginalEstimate":12,"Microsoft.VSTS.Common.StackRank":3}}]')

        item = items[0]
        self.assertEqual((42, 10, "u1", Decimal(12), Decimal(3)),
                         (item.id, item.parent_id, item.assigned_to_id, item.remaining_work, item.order))
        self.assertEqual(datetime.fromisoformat("2026-10-05T09:00:00Z"), item.created_at)

    def test_mantem_esforco_restante_zero_sem_usar_estimativa_original(self):
        item = ler_itens('[{"fields":{"System.Id":42,"Microsoft.VSTS.Scheduling.RemainingWork":0,"Microsoft.VSTS.Scheduling.OriginalEstimate":12}}]')[0]

        self.assertEqual(Decimal(0), item.remaining_work)

    def test_monta_atribuicao_trabalho_pausa_retomada_e_conclusao_das_revisoes(self):
        periods = montar_periodos_de_atividade(ler_revisoes("""
        {"value":[
          {"fields":{"System.State":"To Do","System.ChangedDate":"2026-10-05T09:00:00Z","System.AssignedTo":{"id":"ana","displayName":"Ana"}}},
          {"fields":{"System.State":"In Progress","System.ChangedDate":"2026-10-06T10:00:00Z","System.AssignedTo":{"id":"ana","displayName":"Ana"}}},
          {"fields":{"System.State":"In Progress","System.ChangedDate":"2026-10-06T11:00:00Z","System.AssignedTo":{"id":"ana","displayName":"Ana"}}},
          {"fields":{"System.State":"To Do","System.ChangedDate":"2026-10-07T12:00:00Z","System.AssignedTo":{"id":"ana","displayName":"Ana"}}},
          {"fields":{"System.State":"In Progress","System.ChangedDate":"2026-10-08T14:00:00Z","System.AssignedTo":{"id":"bia","displayName":"Bia"}}},
          {"fields":{"System.State":"Done","System.ChangedDate":"2026-10-09T16:00:00Z","System.AssignedTo":{"id":"bia","displayName":"Bia"}}}
        ]}
        """))

        self.assertEqual(["Assigned", "Active", "Paused", "Active", "Done"], [item.kind for item in periods])
        self.assertEqual("ana", periods[1].member_id)
        self.assertEqual(datetime.fromisoformat("2026-10-07T12:00:00Z"), periods[1].ended_at)
        self.assertEqual("bia", periods[3].member_id)
        self.assertEqual(datetime.fromisoformat("2026-10-09T16:00:00Z"), periods[3].ended_at)


if __name__ == "__main__":
    unittest.main()
