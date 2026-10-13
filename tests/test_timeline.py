import unittest
from datetime import date, datetime, timedelta, timezone
from decimal import Decimal

from sprintflow.forecast import ForecastEngine
from sprintflow.models import (ForecastInput, ForecastMember, ForecastStory, ForecastTask, SprintForecastDataset,
                               SprintSummary, SquadRole, TaskActivityPeriod, WorkStage)
from sprintflow.timeline import TimelineService

START = date(2026, 8, 24)


class Source:
    def __init__(self, dataset: SprintForecastDataset) -> None:
        self.dataset = dataset

    def listar(self):
        return [self.dataset.sprint]

    def obter(self, sprint_id):
        return self.dataset


def sprint(end: date) -> SprintSummary:
    return SprintSummary("sprint", "Sprint", START, end, "Future")


def obter(dataset: SprintForecastDataset):
    return TimelineService(Source(dataset), ForecastEngine()).obter("sprint")


class TimelineServiceTests(unittest.TestCase):
    def test_separa_folgas_coletivas_das_individuais_e_considera_ambas_na_previsao(self):
        entrada = ForecastInput(START, START + timedelta(days=4), START,
                                [ForecastMember("ana", "Ana", SquadRole.Backend, Decimal(8), frozenset({START + timedelta(days=1)})),
                                 ForecastMember("bia", "Bia", SquadRole.Backend, Decimal(8))],
                                [ForecastStory(100, "US", Decimal(1))],
                                [ForecastTask(1, 100, "API A", "ana", WorkStage.Backend, Decimal(8), "Todo"),
                                 ForecastTask(2, 100, "API B", "bia", WorkStage.Backend, Decimal(8), "Todo")])
        dataset = SprintForecastDataset(sprint(entrada.sprint_end), entrada, team_days_off=[START])

        response = obter(dataset)

        ana = next(person for person in response.people if person.id == "ana")
        bia = next(person for person in response.people if person.id == "bia")
        self.assertEqual([START], response.team_days_off)
        self.assertEqual([START + timedelta(days=1)], ana.days_off)
        self.assertEqual([], bia.days_off)
        self.assertEqual([START + timedelta(days=2)], [item.date for item in ana.tasks[0].segments])
        self.assertEqual([START + timedelta(days=1)], [item.date for item in bia.tasks[0].segments])
        self.assertNotIn(START, entrada.members[0].days_off)

    def test_inclui_folgas_de_pessoas_sem_tarefas_e_lista_vazia_quando_nao_ha_folgas(self):
        entrada = ForecastInput(START, START + timedelta(days=11), START,
                                [ForecastMember("ana", "Ana", SquadRole.Backend, Decimal(8), frozenset({START + timedelta(days=2), START})),
                                 ForecastMember("bia", "Bia", SquadRole.Frontend, Decimal(8))],
                                [ForecastStory(100, "US", Decimal(1))],
                                [ForecastTask(1, 100, "Sem responsável", None, WorkStage.Backend, Decimal(0), "Todo")])

        response = obter(SprintForecastDataset(sprint(entrada.sprint_end), entrada))

        ana = next(person for person in response.people if person.id == "ana")
        self.assertEqual([], response.team_days_off)
        self.assertEqual([START, START + timedelta(days=2)], ana.days_off)
        self.assertEqual([], ana.tasks)
        self.assertEqual([], next(person for person in response.people if person.id == "bia").days_off)
        self.assertEqual([], next(person for person in response.people if person.id == "").days_off)

    def test_conta_historias_concluidas_pelo_estado_da_historia_independentemente_das_tarefas(self):
        cases = [("Done", None, True), ("Todo", None, False), ("Unknown", None, False), ("Done", "Review", True),
                 ("Todo", "Done", False), ("Testing", "Done", False), ("Awaiting Release", "Done", False)]
        for story_state, task_state, expected in cases:
            with self.subTest(story_state=story_state, task_state=task_state):
                tasks = [] if task_state is None else [ForecastTask(1, 100, "Task", None, WorkStage.Backend, Decimal(0), task_state)]
                entrada = ForecastInput(START, START + timedelta(days=11), START, [],
                                        [ForecastStory(100, "US", Decimal(1), story_state)], tasks)

                response = obter(SprintForecastDataset(sprint(entrada.sprint_end), entrada))

                story = response.stories[0]
                self.assertEqual((expected, story_state), (story.is_done, story.state))
                self.assertEqual((1 if expected else 0, 1), (response.summary.completed_stories, response.summary.total_stories))

    def test_preserva_dados_da_tarefa_com_e_sem_previsao_e_responsavel(self):
        cases = [("ana", 8, True, False), ("ana", 120, True, True), ("ana", 0, False, False),
                 (None, 8, False, False), ("fora-da-squad", 8, False, False)]
        for member_id, remaining_work, has_forecast, exceeds_sprint in cases:
            with self.subTest(member_id=member_id, remaining_work=remaining_work):
                task = ForecastTask(1, 100, "Criar API", member_id, WorkStage.Backend, Decimal(remaining_work), "In Progress", Decimal(2))
                entrada = ForecastInput(START, START + timedelta(days=11), START,
                                        [ForecastMember("ana", "Ana", SquadRole.Backend, Decimal(8))],
                                        [ForecastStory(100, "Minha US", Decimal(1), description="<p>Descrição completa</p>")], [task])
                activity = TaskActivityPeriod(member_id, "Ana", "Active", datetime(2026, 8, 24, 9, tzinfo=timezone.utc), None)
                dataset = SprintForecastDataset(sprint(entrada.sprint_end), entrada,
                                                {1: "https://example.com/workitems/1", 100: "https://example.com/workitems/100"},
                                                {1: [activity]})

                response = obter(dataset)

                story = response.stories[0]
                self.assertEqual(("<p>Descrição completa</p>", "https://example.com/workitems/100"), (story.description, story.azure_url))
                owner = next(person for person in response.people if person.tasks)
                result = owner.tasks[0]
                self.assertEqual("ana" if member_id == "ana" else "", owner.id)
                self.assertEqual((task.id, task.story_id, "Minha US", task.title, task.stage, task.state, task.remaining_work, task.order),
                                 (result.id, result.story_id, result.story_title, result.title, result.stage, result.state,
                                  result.remaining_work, result.order))
                self.assertEqual("https://example.com/workitems/1", result.azure_url)
                self.assertEqual([activity], result.activity)
                self.assertEqual(has_forecast, len(result.segments) > 0)
                self.assertEqual(remaining_work if has_forecast else 0, sum(segment.hours for segment in result.segments))
                self.assertEqual(exceeds_sprint, result.exceeds_sprint)


if __name__ == "__main__":
    unittest.main()
