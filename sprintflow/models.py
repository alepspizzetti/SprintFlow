from __future__ import annotations

from dataclasses import dataclass, field, fields, is_dataclass
from datetime import date, datetime, time
from decimal import Decimal
from enum import IntEnum


class SquadRole(IntEnum):
    Backend = 0
    Frontend = 1
    QA = 2
    PD = 3
    EL = 4
    GPM = 5
    PM = 6


class WorkStage(IntEnum):
    Backend = 0
    Frontend = 1
    QA = 2
    Other = 3


@dataclass(frozen=True)
class ForecastOptions:
    workday_start: time = time(8, 0)
    workday_end: time = time(18, 0)


@dataclass(frozen=True)
class ForecastMember:
    id: str
    name: str
    role: SquadRole
    daily_capacity_hours: Decimal
    days_off: frozenset[date] | None = None


@dataclass(frozen=True)
class ForecastStory:
    id: int
    title: str
    order: Decimal
    state: str = "Unknown"
    description: str | None = None


@dataclass(frozen=True)
class ForecastTask:
    id: int
    story_id: int
    title: str
    assigned_member_id: str | None
    stage: WorkStage
    remaining_work: Decimal
    state: str
    order: Decimal | None = None
    created_at: datetime | None = None
    started_at: datetime | None = None


@dataclass(frozen=True)
class ForecastInput:
    sprint_start: date
    sprint_end: date
    forecast_date: date = field(metadata={"json": "planningDate"})
    members: list[ForecastMember] = field(default_factory=list)
    stories: list[ForecastStory] = field(default_factory=list)
    tasks: list[ForecastTask] = field(default_factory=list)
    now: datetime | None = None


@dataclass(frozen=True)
class AllocationSegment:
    date: date
    hours: Decimal
    start: datetime
    end: datetime


@dataclass(frozen=True)
class TaskForecast:
    id: int
    story_id: int
    title: str
    assigned_member_id: str
    stage: WorkStage
    state: str
    segments: list[AllocationSegment]
    start_date: date | None
    end_date: date | None
    exceeds_sprint: bool


@dataclass(frozen=True)
class FlowBlockage:
    affected_member_id: str
    affected_task_id: int
    blocking_task_id: int | None
    date: date
    blocked_hours: Decimal
    cause: str


@dataclass(frozen=True)
class ForecastResult:
    tasks: list[TaskForecast]
    blockages: list[FlowBlockage]
    ignored_task_ids: list[int]


@dataclass(frozen=True)
class SprintSummary:
    id: str
    name: str
    start_date: date
    end_date: date
    status: str


@dataclass(frozen=True)
class TaskActivityPeriod:
    member_id: str | None
    member_name: str | None
    kind: str
    started_at: datetime
    ended_at: datetime | None


@dataclass(frozen=True)
class SprintForecastDataset:
    sprint: SprintSummary
    input: ForecastInput
    work_item_urls: dict[int, str] | None = None
    activity: dict[int, list[TaskActivityPeriod]] | None = None
    synced_at: datetime | None = None
    team_days_off: list[date] | None = None


@dataclass(frozen=True)
class TimelineStory:
    id: int
    title: str
    order: Decimal
    completed_tasks: int
    total_tasks: int
    is_done: bool = False
    state: str = "Todo"
    description: str | None = None
    azure_url: str | None = None


@dataclass(frozen=True)
class TimelineSegment:
    date: date
    hours: Decimal
    start: datetime
    end: datetime


@dataclass(frozen=True)
class TimelineTask:
    id: int
    story_id: int
    story_title: str
    title: str
    stage: WorkStage
    state: str
    remaining_work: Decimal
    order: Decimal | None
    segments: list[TimelineSegment]
    exceeds_sprint: bool
    azure_url: str | None
    activity: list[TaskActivityPeriod]


@dataclass(frozen=True)
class TimelineBlockage:
    task_id: int
    blocking_task_id: int | None
    date: date
    hours: Decimal
    cause: str


@dataclass(frozen=True)
class TimelinePerson:
    id: str
    name: str
    role: SquadRole
    daily_capacity_hours: Decimal
    tasks: list[TimelineTask]
    blockages: list[TimelineBlockage]
    days_off: list[date]


@dataclass(frozen=True)
class TimelineSummary:
    completed_stories: int
    total_stories: int
    forecast_end: date | None


@dataclass(frozen=True)
class TimelineResponse:
    sprint: SprintSummary
    today: date
    dates: list[date]
    stories: list[TimelineStory]
    people: list[TimelinePerson]
    summary: TimelineSummary
    ignored_task_count: int
    synced_at: datetime | None
    team_days_off: list[date]


def para_json(value: object, enums_by_name: bool = True) -> object:
    if is_dataclass(value):
        return {item.metadata.get("json", _camel(item.name)): para_json(getattr(value, item.name), enums_by_name)
                for item in fields(value)}
    if isinstance(value, IntEnum):
        return value.name if enums_by_name else int(value)
    if isinstance(value, Decimal):
        return int(value) if value == value.to_integral_value() else float(value)
    if isinstance(value, (datetime, date)):
        return value.isoformat()
    if isinstance(value, dict):
        return {str(key): para_json(item, enums_by_name) for key, item in value.items()}
    if isinstance(value, (frozenset, set)):
        return [para_json(item, enums_by_name) for item in sorted(value)]
    if isinstance(value, (list, tuple)):
        return [para_json(item, enums_by_name) for item in value]
    return value


def ler_dataset(data: dict) -> SprintForecastDataset:
    entrada = data["input"]
    activity = data.get("activity")
    return SprintForecastDataset(
        sprint=ler_sprint(data["sprint"]),
        input=ForecastInput(
            sprint_start=date.fromisoformat(entrada["sprintStart"]),
            sprint_end=date.fromisoformat(entrada["sprintEnd"]),
            forecast_date=date.fromisoformat(entrada["planningDate"]),
            members=[_ler_membro(item) for item in entrada.get("members") or []],
            stories=[_ler_historia(item) for item in entrada.get("stories") or []],
            tasks=[_ler_tarefa(item) for item in entrada.get("tasks") or []],
            now=_ler_data_hora(entrada.get("now"))),
        work_item_urls={int(key): value for key, value in (data.get("workItemUrls") or {}).items()},
        activity=None if activity is None else {
            int(key): [_ler_periodo(item) for item in items] for key, items in activity.items()},
        synced_at=_ler_data_hora(data.get("syncedAt")),
        team_days_off=[date.fromisoformat(item) for item in data.get("teamDaysOff") or []])


def ler_sprint(data: dict) -> SprintSummary:
    return SprintSummary(data["id"], data["name"], date.fromisoformat(data["startDate"]),
                         date.fromisoformat(data["endDate"]), data["status"])


def _ler_membro(data: dict) -> ForecastMember:
    return ForecastMember(data["id"], data["name"], _ler_enum(SquadRole, data["role"]),
                          Decimal(str(data["dailyCapacityHours"])),
                          frozenset(date.fromisoformat(item) for item in data.get("daysOff") or []))


def _ler_historia(data: dict) -> ForecastStory:
    return ForecastStory(data["id"], data["title"], Decimal(str(data["order"])),
                         data.get("state") or "Unknown", data.get("description"))


def _ler_tarefa(data: dict) -> ForecastTask:
    return ForecastTask(data["id"], data["storyId"], data["title"], data.get("assignedMemberId"),
                        _ler_enum(WorkStage, data["stage"]), Decimal(str(data["remainingWork"])), data["state"],
                        None if data.get("order") is None else Decimal(str(data["order"])),
                        _ler_data_hora(data.get("createdAt")), _ler_data_hora(data.get("startedAt")))


def _ler_periodo(data: dict) -> TaskActivityPeriod:
    return TaskActivityPeriod(data.get("memberId"), data.get("memberName"), data["kind"],
                              _ler_data_hora(data["startedAt"]), _ler_data_hora(data.get("endedAt")))


def _ler_enum(kind: type[IntEnum], value: int | str) -> IntEnum:
    return kind(value) if isinstance(value, int) else kind[value]


def _ler_data_hora(value: str | None) -> datetime | None:
    return None if value is None else datetime.fromisoformat(value)


def _camel(name: str) -> str:
    head, *tail = name.split("_")
    return head + "".join(part.capitalize() for part in tail)
