from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from datetime import date, datetime, timedelta, timezone
from decimal import Decimal

from .azure_cli import AzureDevOpsCliClient, AzureDevOpsReadException, AzureWorkItem, AzureWorkItemRevision, ler_data_hora
from .models import (ForecastInput, ForecastMember, ForecastStory, ForecastTask, SprintForecastDataset, SprintSummary,
                     SquadRole, TaskActivityPeriod, WorkStage)


class AzureDevOpsSprintForecastSource:
    def __init__(self, client: AzureDevOpsCliClient) -> None:
        self._client = client

    def listar(self) -> list[SprintSummary]:
        iterations = [item for item in self._client.obter_iteracoes() if _tem_datas(item)]
        return sorted((_converter_para_resumo(item) for item in iterations), key=lambda item: item.start_date)

    def obter(self, sprint_id: str) -> SprintForecastDataset | None:
        iterations = self._client.obter_iteracoes()
        iteration = next((item for item in iterations if str(item.get("id", "")).lower() == sprint_id.lower()), None)
        if iteration is None:
            raise AzureDevOpsReadException(
                f"A sprint solicitada não foi encontrada entre as {len(iterations)} iterações disponíveis para o time configurado.")
        if not iteration.get("path") or not _tem_datas(iteration):
            raise AzureDevOpsReadException("A sprint solicitada não possui caminho ou datas válidos no Azure DevOps.")

        sprint = _converter_para_resumo(iteration)
        with ThreadPoolExecutor(max_workers=4) as executor:
            members_future = executor.submit(self._client.obter_membros_do_time)
            capacities_future = executor.submit(self._carregar_capacidades, iteration["id"])
            team_days_off_future = executor.submit(self._client.obter_folgas_do_time, iteration["id"])
            items_future = executor.submit(self._client.obter_itens, iteration["path"])
            capacities = {str(item["teamMember"]["id"]).lower(): item for item in capacities_future.result()}
            members = [_converter_para_membro(item["identity"], capacities.get(str(item["identity"]["id"]).lower()))
                       for item in members_future.result()]
            items = items_future.result()
            team_days_off = team_days_off_future.result()

        story_items = {item.id: item for item in items if _eh_historia(item.type)}
        task_items = [item for item in items if _eh_tarefa(item.type)]
        activity: dict[int, list[TaskActivityPeriod]] = {}
        if sprint.status == "Current":
            revisions = self._client.obter_revisoes_dos_itens([item.id for item in task_items])
            activity = {key: montar_periodos_de_atividade(value) for key, value in revisions.items()}
        for missing in dict.fromkeys(task.parent_id for task in task_items
                                     if task.parent_id is not None and task.parent_id not in story_items):
            story_items[missing] = AzureWorkItem(missing, None, "User Story", f"US #{missing}", "Unknown", None, Decimal(0), None)

        ordered = sorted(story_items.values(), key=lambda item: (item.order is None, item.order or 0, item.id))
        stories = [ForecastStory(item.id, item.title, item.order if item.order is not None else Decimal(index + 1),
                                 normalizar_estado_da_tarefa(item.state), item.description)
                   for index, item in enumerate(ordered)]
        tasks = [ForecastTask(item.id, item.parent_id, item.title, item.assigned_to_id, obter_etapa(item, members),
                              item.remaining_work, normalizar_estado_da_tarefa(item.state), item.order, item.created_at)
                 for item in task_items if item.parent_id is not None]

        today = date.today()
        forecast_date = sprint.start_date if today < sprint.start_date else today
        return SprintForecastDataset(
            sprint,
            ForecastInput(sprint.start_date, sprint.end_date, forecast_date, members, stories, tasks),
            {item.id: self._client.obter_url_do_item(item.id) for item in items},
            activity,
            team_days_off=sorted({day for item in team_days_off for day in _enumerar_datas(item)}))

    def _carregar_capacidades(self, iteration_id: str) -> list[dict]:
        try:
            return self._client.obter_capacidades(iteration_id)
        except AzureDevOpsReadException:
            return []


def montar_periodos_de_atividade(revisions: list[AzureWorkItemRevision]) -> list[TaskActivityPeriod]:
    changes: list[AzureWorkItemRevision] = []
    for revision in sorted(revisions, key=lambda item: item.changed_at):
        state = normalizar_estado_da_tarefa(revision.state)
        if changes and changes[-1].state == state and changes[-1].assigned_to_id == revision.assigned_to_id:
            continue
        changes.append(AzureWorkItemRevision(state, revision.changed_at, revision.assigned_to_id, revision.assigned_to_name))

    periods = []
    has_started = False
    for index, change in enumerate(changes):
        kind = {"In Progress": "Active", "Done": "Done", "Review": "Review"}.get(
            change.state, "Paused" if has_started else "Assigned")
        if kind in ("Active", "Review", "Done"):
            has_started = True
        ended_at = changes[index + 1].changed_at if index + 1 < len(changes) else None
        periods.append(TaskActivityPeriod(change.assigned_to_id, change.assigned_to_name, kind, change.changed_at, ended_at))
    return periods


def obter_etapa(item: AzureWorkItem, members: list[ForecastMember]) -> WorkStage:
    text = item.title.lower()
    if "front" in text or " ui" in text:
        return WorkStage.Frontend
    if "qa" in text or "test" in text:
        return WorkStage.QA
    if "back" in text or "api" in text:
        return WorkStage.Backend
    owner_role = next((member.role for member in members if member.id == item.assigned_to_id), None)
    return {SquadRole.Backend: WorkStage.Backend, SquadRole.Frontend: WorkStage.Frontend,
            SquadRole.QA: WorkStage.QA}.get(owner_role, WorkStage.Other)


def normalizar_estado_da_tarefa(state: str) -> str:
    return {"new": "Todo", "to do": "Todo", "todo": "Todo",
            "active": "In Progress", "in progress": "In Progress", "doing": "In Progress",
            "resolved": "Review", "review": "Review",
            "closed": "Done", "done": "Done"}.get(state.lower(), state)


def _converter_para_resumo(item: dict) -> SprintSummary:
    attributes = item["attributes"]
    start = _data_utc(attributes["startDate"])
    end = _data_utc(attributes["finishDate"])
    time_frame = (attributes.get("timeFrame") or "").strip().lower()
    today = date.today()
    status = {"current": "Current", "future": "Future", "past": "Past"}.get(
        time_frame, "Future" if start > today else "Past" if end < today else "Current")
    return SprintSummary(item["id"], item["name"], start, end, status)


def _converter_para_membro(identity: dict, capacity: dict | None) -> ForecastMember:
    activities = (capacity or {}).get("activities") or []
    activity = max(activities, key=lambda item: Decimal(str(item.get("capacityPerDay") or 0)), default=None)
    days_off = frozenset(day for item in (capacity or {}).get("daysOff") or [] for day in _enumerar_datas(item))
    role = _obter_papel(activity.get("name") if activity else None) or SquadRole.PD
    capacity_per_day = Decimal(str(activity.get("capacityPerDay") or 0)) if activity else Decimal(0)
    return ForecastMember(identity["id"], identity.get("displayName") or "", role, capacity_per_day, days_off)


def _enumerar_datas(item: dict) -> list[date]:
    start = ler_data_hora(item.get("start"))
    end = ler_data_hora(item.get("end"))
    if start is None or end is None:
        return []
    return [start.date() + timedelta(days=offset) for offset in range((end.date() - start.date()).days + 1)]


def _obter_papel(value: str | None) -> SquadRole | None:
    if value is None:
        return None
    name = value.lower()
    if "back" in name:
        return SquadRole.Backend
    if "front" in name:
        return SquadRole.Frontend
    if "qa" in name or "test" in name:
        return SquadRole.QA
    return {"pd": SquadRole.PD, "el": SquadRole.EL, "gpm": SquadRole.GPM, "pm": SquadRole.PM}.get(name)


def _tem_datas(item: dict) -> bool:
    attributes = item.get("attributes") or {}
    return attributes.get("startDate") is not None and attributes.get("finishDate") is not None


def _data_utc(value: str) -> date:
    parsed = datetime.fromisoformat(value)
    return (parsed.astimezone(timezone.utc) if parsed.tzinfo else parsed).date()


def _eh_historia(kind: str) -> bool:
    return kind.lower() in ("user story", "product backlog item")


def _eh_tarefa(kind: str) -> bool:
    return kind.lower() in ("task", "bug")
