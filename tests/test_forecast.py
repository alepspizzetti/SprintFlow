import json
import unittest
from dataclasses import replace
from datetime import date, datetime, time, timedelta
from decimal import Decimal

from sprintflow.forecast import ForecastEngine
from sprintflow.models import ForecastInput, ForecastMember, ForecastStory, ForecastTask, SquadRole, WorkStage, ler_dataset, para_json

MONDAY = date(2026, 8, 24)


class ForecastEngineTests(unittest.TestCase):
    def test_preserva_data_da_previsao_do_cache_existente(self):
        data = {"sprint": {"id": "s", "name": "S", "startDate": "2026-08-24", "endDate": "2026-09-04", "status": "Future"},
                "input": {"sprintStart": "2026-08-24", "sprintEnd": "2026-09-04", "planningDate": "2026-08-25",
                          "members": [], "stories": [], "tasks": []}}

        entrada = ler_dataset(data).input

        self.assertEqual(MONDAY + timedelta(days=1), entrada.forecast_date)
        self.assertIn('"planningDate": "2026-08-25"', json.dumps(para_json(entrada)))

    def test_distribui_tarefa_pela_capacidade_diaria_do_membro(self):
        result = calcular([membro("back", SquadRole.Backend, 8)], [tarefa(1, "back", WorkStage.Backend, 12)])

        segments = single(result.tasks).segments
        self.assertEqual([(MONDAY, 8), (MONDAY + timedelta(days=1), 4)], [(item.date, item.hours) for item in segments])

    def test_ignora_fins_de_semana_e_folgas(self):
        friday = date(2026, 8, 28)
        member = ForecastMember("back", "Backend", SquadRole.Backend, Decimal(8), frozenset({friday + timedelta(days=3)}))
        entrada = criar_entrada(friday, [member], [tarefa(1, "back", WorkStage.Backend, 16)])

        task = single(ForecastEngine().calcular_previsao(entrada).tasks)

        self.assertEqual([friday, friday + timedelta(days=4)], [item.date for item in task.segments])

    def test_cria_bloqueio_do_frontend_ate_backend_estar_disponivel(self):
        result = calcular([membro("back", SquadRole.Backend, 8), membro("front", SquadRole.Frontend, 8)],
                          [tarefa(1, "back", WorkStage.Backend, 16), tarefa(2, "front", WorkStage.Frontend, 8)])

        blockage = result.blockages[0]
        self.assertEqual(2, len(result.blockages))
        self.assertEqual(("front", 2, 1, MONDAY),
                         (blockage.affected_member_id, blockage.affected_task_id, blockage.blocking_task_id, blockage.date))

    def test_qa_aguarda_backend_e_frontend_da_mesma_historia(self):
        result = calcular([membro("back", SquadRole.Backend, 8), membro("front", SquadRole.Frontend, 8), membro("qa", SquadRole.QA, 8)],
                          [tarefa(1, "back", WorkStage.Backend, 8), tarefa(2, "front", WorkStage.Frontend, 8),
                           tarefa(3, "qa", WorkStage.QA, 8)])

        frontend = by_id(result.tasks, 2)
        qa = by_id(result.tasks, 3)
        self.assertGreaterEqual(qa.start_date, frontend.end_date)
        self.assertTrue(any(item.affected_task_id == 3 for item in result.blockages))

    def test_ignora_tarefas_sem_responsavel(self):
        result = calcular([membro("back", SquadRole.Backend, 8)],
                          [ForecastTask(1, 100, "Sem responsável", None, WorkStage.Backend, Decimal(8), "Todo")])

        self.assertEqual([], result.tasks)
        self.assertEqual([1], result.ignored_task_ids)

    def test_ignora_tarefas_sem_capacidade_diaria(self):
        result = calcular([membro("back", SquadRole.Backend, 0)], [tarefa(1, "back", WorkStage.Backend, 8)])

        self.assertEqual([], result.tasks)
        self.assertEqual([1], result.ignored_task_ids)

    def test_respeita_ordem_das_tarefas_do_azure_dentro_da_historia(self):
        result = calcular([membro("back", SquadRole.Backend, 8)],
                          [ForecastTask(1, 100, "Depois", "back", WorkStage.Backend, Decimal(8), "Todo", Decimal(2)),
                           ForecastTask(2, 100, "Antes", "back", WorkStage.Backend, Decimal(8), "Todo", Decimal(1))])

        self.assertEqual(MONDAY, by_id(result.tasks, 2).start_date)
        self.assertEqual(MONDAY + timedelta(days=1), by_id(result.tasks, 1).start_date)

    def test_enfileira_tarefas_da_mesma_pessoa_sem_sobreposicao(self):
        result = calcular([membro("back", SquadRole.Backend, 8)],
                          [tarefa(1, "back", WorkStage.Backend, 2), tarefa(2, "back", WorkStage.Backend, 2)])

        first = single(by_id(result.tasks, 1).segments)
        second = single(by_id(result.tasks, 2).segments)
        self.assertEqual(hora(8), first.start)
        self.assertEqual(hora(10, 30), first.end)
        self.assertEqual(first.end, second.start)

    def test_tarefa_a_fazer_inicia_no_maior_entre_cadastro_agora_e_fim_da_anterior(self):
        entrada = replace(criar_entrada(MONDAY, [membro("back", SquadRole.Backend, 8)],
                                        [replace(tarefa(1, "back", WorkStage.Backend, 2), created_at=hora(9).astimezone()),
                                         replace(tarefa(2, "back", WorkStage.Backend, 2), created_at=hora(16).astimezone())]),
                          now=hora(13))

        result = ForecastEngine().calcular_previsao(entrada)

        self.assertEqual(hora(13), by_id(result.tasks, 1).segments[0].start)
        self.assertEqual(hora(16), by_id(result.tasks, 2).segments[0].start)

    def test_tarefa_em_andamento_ocupa_a_pessoa_antes_da_fila_independente_da_ordem(self):
        entrada = replace(criar_entrada(MONDAY, [membro("back", SquadRole.Backend, 8)],
                                        [ForecastTask(1, 100, "Primeira", "back", WorkStage.Backend, Decimal(2), "Todo", Decimal(1)),
                                         ForecastTask(2, 100, "Segunda", "back", WorkStage.Backend, Decimal(2), "In Progress",
                                                      Decimal(2), started_at=hora(9).astimezone())]),
                          now=hora(10))

        result = ForecastEngine().calcular_previsao(entrada)

        active = single(by_id(result.tasks, 2).segments)
        self.assertEqual(hora(10), active.start)
        self.assertEqual(active.end, by_id(result.tasks, 1).segments[0].start)

    def test_tarefas_em_andamento_da_mesma_pessoa_ficam_sobrepostas(self):
        started_at = hora(9).astimezone()
        entrada = replace(criar_entrada(MONDAY, [membro("back", SquadRole.Backend, 8)],
                                        [ForecastTask(1, 100, "A", "back", WorkStage.Backend, Decimal(2), "In Progress",
                                                      Decimal(1), started_at=started_at),
                                         ForecastTask(2, 100, "B", "back", WorkStage.Backend, Decimal(2), "In Progress",
                                                      Decimal(2), started_at=started_at)]),
                          now=hora(10))

        result = ForecastEngine().calcular_previsao(entrada)

        self.assertTrue(all(task.segments[0].start == hora(10) for task in result.tasks))


def hora(hour: int, minute: int = 0) -> datetime:
    return datetime.combine(MONDAY, time(hour, minute))


def calcular(members, tasks):
    return ForecastEngine().calcular_previsao(criar_entrada(MONDAY, members, tasks))


def criar_entrada(start: date, members, tasks) -> ForecastInput:
    return ForecastInput(start, start + timedelta(days=11), start, members, [ForecastStory(100, "US", Decimal(1))], tasks)


def membro(member_id: str, role: SquadRole, capacity: int) -> ForecastMember:
    return ForecastMember(member_id, member_id, role, Decimal(capacity))


def tarefa(task_id: int, member: str, stage: WorkStage, hours: int) -> ForecastTask:
    return ForecastTask(task_id, 100, f"Task {task_id}", member, stage, Decimal(hours), "Todo")


def by_id(items, item_id):
    return next(item for item in items if item.id == item_id)


def single(items):
    assert len(items) == 1, items
    return items[0]


if __name__ == "__main__":
    unittest.main()
