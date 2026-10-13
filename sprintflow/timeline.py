from __future__ import annotations

from dataclasses import replace
from datetime import date, datetime, timedelta
from decimal import Decimal
from typing import Protocol

from .forecast import ForecastEngine
from .models import (ForecastInput, ForecastResult, ForecastTask, SprintForecastDataset, SprintSummary, SquadRole,
                     TaskForecast, TimelineBlockage, TimelinePerson, TimelineResponse, TimelineSegment, TimelineStory,
                     TimelineSummary, TimelineTask)


class SprintForecastSource(Protocol):
    def listar(self) -> list[SprintSummary]: ...

    def obter(self, sprint_id: str) -> SprintForecastDataset | None: ...


class TimelineService:
    def __init__(self, source: SprintForecastSource, engine: ForecastEngine) -> None:
        self._source = source
        self._engine = engine

    def listar_sprints(self) -> list[SprintSummary]:
        return self._source.listar()

    def sincronizar(self, sprint_id: str) -> TimelineResponse | None:
        atualizar = getattr(self._source, "atualizar", None)
        if atualizar is not None and not atualizar(sprint_id):
            return None
        return self.obter(sprint_id)

    def obter(self, sprint_id: str) -> TimelineResponse | None:
        dataset = self._source.obter(sprint_id)
        if dataset is None:
            return None
        now = datetime.now()
        entrada = _montar_entrada(dataset, now)
        forecast = self._engine.calcular_previsao(entrada)
        stories = _montar_historias(dataset, entrada)
        forecast_end = max((task.end_date for task in forecast.tasks if task.end_date is not None), default=None)
        return TimelineResponse(
            dataset.sprint, entrada.forecast_date, _obter_dias_uteis(dataset.sprint.start_date, dataset.sprint.end_date),
            stories, _montar_pessoas(dataset, entrada, forecast),
            TimelineSummary(sum(1 for story in stories if story.is_done), len(stories), forecast_end),
            len(forecast.ignored_task_ids), dataset.synced_at, dataset.team_days_off or [])


def _montar_entrada(dataset: SprintForecastDataset, now: datetime) -> ForecastInput:
    today = now.date()
    sprint = dataset.sprint
    if sprint.status == "Current":
        forecast_date = sprint.start_date if today < sprint.start_date else sprint.end_date if today > sprint.end_date else today
    else:
        forecast_date = sprint.start_date
    team_days_off = dataset.team_days_off or []
    return replace(
        dataset.input,
        forecast_date=forecast_date,
        now=now if forecast_date == today else None,
        tasks=[replace(task, started_at=_obter_inicio_real(dataset, task)) for task in dataset.input.tasks],
        members=[replace(member, days_off=frozenset(member.days_off or ()) | frozenset(team_days_off))
                 for member in dataset.input.members])


def _obter_inicio_real(dataset: SprintForecastDataset, task: ForecastTask) -> datetime | None:
    periods = (dataset.activity or {}).get(task.id) or []
    return next((period.started_at for period in periods if period.kind == "Active"), None)


def _montar_pessoas(dataset: SprintForecastDataset, entrada: ForecastInput,
                    forecast: ForecastResult) -> list[TimelinePerson]:
    tasks_by_id = {task.id: task for task in entrada.tasks}
    stories_by_id = {story.id: story for story in entrada.stories}
    ignored = set(forecast.ignored_task_ids)
    people = []
    for member in dataset.input.members:
        tasks = [_montar_tarefa(tasks_by_id[task.id], stories_by_id[task.story_id].title, dataset, task)
                 for task in forecast.tasks if task.assigned_member_id == member.id]
        tasks += [_montar_tarefa(task, stories_by_id[task.story_id].title, dataset)
                  for task in entrada.tasks if task.assigned_member_id == member.id and task.id in ignored]
        blockages = [TimelineBlockage(item.affected_task_id, item.blocking_task_id, item.date, item.blocked_hours, item.cause)
                     for item in forecast.blockages if item.affected_member_id == member.id]
        people.append(TimelinePerson(member.id, member.name, member.role, member.daily_capacity_hours, tasks, blockages,
                                     sorted(member.days_off or ())))
    member_ids = {member.id for member in entrada.members}
    without_member = [task for task in entrada.tasks
                      if task.assigned_member_id is None or task.assigned_member_id not in member_ids]
    if without_member:
        people.append(TimelinePerson("", "Sem responsável na squad", SquadRole.PD, Decimal(0),
                                     [_montar_tarefa(task, stories_by_id[task.story_id].title, dataset)
                                      for task in without_member], [], []))
    return people


def _montar_historias(dataset: SprintForecastDataset, entrada: ForecastInput) -> list[TimelineStory]:
    urls = dataset.work_item_urls or {}
    stories = []
    for story in sorted(entrada.stories, key=lambda item: (item.order, item.id)):
        tasks = [task for task in entrada.tasks if task.story_id == story.id]
        stories.append(TimelineStory(story.id, story.title, story.order, sum(1 for task in tasks if task.state == "Done"),
                                     len(tasks), story.state == "Done", story.state, story.description, urls.get(story.id)))
    return stories


def _montar_tarefa(task: ForecastTask, story_title: str, dataset: SprintForecastDataset,
                   forecast: TaskForecast | None = None) -> TimelineTask:
    segments = [] if forecast is None else [
        TimelineSegment(segment.date, segment.hours, segment.start, segment.end) for segment in forecast.segments]
    return TimelineTask(task.id, task.story_id, story_title, task.title, task.stage, task.state, task.remaining_work,
                        task.order, segments, forecast is not None and forecast.exceeds_sprint,
                        (dataset.work_item_urls or {}).get(task.id), (dataset.activity or {}).get(task.id) or [])


def _obter_dias_uteis(start: date, end: date) -> list[date]:
    return [start + timedelta(days=offset) for offset in range((end - start).days + 1)
            if (start + timedelta(days=offset)).weekday() < 5]
