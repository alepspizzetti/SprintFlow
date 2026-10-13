from __future__ import annotations

from dataclasses import dataclass
from datetime import date, datetime, timedelta
from decimal import ROUND_HALF_EVEN, Decimal

from .models import (AllocationSegment, FlowBlockage, ForecastInput, ForecastMember, ForecastOptions,
                     ForecastResult, ForecastTask, TaskForecast, WorkStage)

_ORDEM_DAS_ETAPAS = {WorkStage.Backend: 0, WorkStage.Frontend: 1, WorkStage.QA: 2}
_DECIMAL_MAXIMO = Decimal("79228162514264337593543950335")


@dataclass
class _Cursor:
    date: date
    daily_capacity: Decimal
    remaining_hours: Decimal


class ForecastEngine:
    def __init__(self, options: ForecastOptions | None = None) -> None:
        self._workday = _validar_expediente(options or ForecastOptions())

    def calcular_previsao(self, entrada: ForecastInput) -> ForecastResult:
        members = {member.id: member for member in entrada.members}
        story_order = {story.id: story.order for story in entrada.stories}
        cursors = {member.id: self._criar_cursor(entrada, member)
                   for member in entrada.members if member.daily_capacity_hours > 0}
        forecasts: dict[int, TaskForecast] = {}
        blockages: list[FlowBlockage] = []
        ignored: list[int] = []

        for task in self._ordenar(entrada.tasks, story_order):
            if _esta_concluida(task):
                forecasts[task.id] = TaskForecast(task.id, task.story_id, task.title, task.assigned_member_id or "",
                                                  task.stage, task.state, [], None, None, False)
                continue
            member = members.get(task.assigned_member_id) if task.assigned_member_id is not None else None
            cursor = cursors.get(task.assigned_member_id) if task.assigned_member_id is not None else None
            if member is None or task.remaining_work <= 0 or member.daily_capacity_hours <= 0 or cursor is None:
                ignored.append(task.id)
                continue

            segments = self._alocar_tarefa(entrada, task, member, cursor, forecasts, blockages)
            start = min((segment.date for segment in segments), default=None)
            end = max((segment.date for segment in segments), default=None)
            forecasts[task.id] = TaskForecast(task.id, task.story_id, task.title, member.id, task.stage, task.state,
                                              segments, start, end, end is not None and end > entrada.sprint_end)

        return ForecastResult(
            sorted(forecasts.values(), key=lambda item: (item.start_date is not None, item.start_date or date.min, item.id)),
            sorted(blockages, key=lambda item: (item.date, item.affected_member_id)),
            ignored)

    @staticmethod
    def _ordenar(tasks: list[ForecastTask], story_order: dict[int, Decimal]) -> list[ForecastTask]:
        return sorted(tasks, key=lambda task: (
            0 if _esta_em_andamento(task) else 1,
            _ORDEM_DAS_ETAPAS.get(task.stage, 3),
            story_order.get(task.story_id, _DECIMAL_MAXIMO),
            _DECIMAL_MAXIMO if task.order is None else task.order,
            task.id))

    def _alocar_tarefa(self, entrada: ForecastInput, task: ForecastTask, member: ForecastMember, cursor: _Cursor,
                       forecasts: dict[int, TaskForecast], blockages: list[FlowBlockage]) -> list[AllocationSegment]:
        related = [item for item in entrada.tasks if item.story_id == task.story_id]
        if _esta_em_andamento(task):
            progress = self._criar_cursor(entrada, member)
            segments = self._alocar(progress, member, task.remaining_work, datetime.min)
            self._mover_para(cursor, self._instante_do_cursor(progress))
            return segments
        if task.stage == WorkStage.Frontend:
            dependencies = [item for item in related if item.stage == WorkStage.Backend and not _esta_concluida(item)]
            return self._alocar_apos_dependencias(cursor, member, task, dependencies, forecasts, blockages,
                                                  "Frontend aguardando conclusão do Backend.")
        if task.stage == WorkStage.QA:
            dependencies = [item for item in related
                            if item.stage in (WorkStage.Backend, WorkStage.Frontend) and not _esta_concluida(item)]
            return self._alocar_apos_dependencias(cursor, member, task, dependencies, forecasts, blockages,
                                                  "QA aguardando todas as tasks de Backend e Frontend da User Story.")
        return self._alocar(cursor, member, task.remaining_work, _hora_local(task.created_at))

    def _alocar_apos_dependencias(self, cursor: _Cursor, member: ForecastMember, task: ForecastTask,
                                  dependencies: list[ForecastTask], forecasts: dict[int, TaskForecast],
                                  blockages: list[FlowBlockage], cause: str) -> list[AllocationSegment]:
        self._mover_para(cursor, _hora_local(task.created_at))
        dependency_end, blocking_task_id = _obter_ultimo_termino(dependencies, forecasts)
        self._adicionar_bloqueios(blockages, cursor, member, task, dependency_end, blocking_task_id, cause)
        return self._alocar(cursor, member, task.remaining_work, dependency_end)

    def _alocar(self, cursor: _Cursor, member: ForecastMember, requested_hours: Decimal,
                not_before: datetime) -> list[AllocationSegment]:
        segments: list[AllocationSegment] = []
        remaining = requested_hours
        self._mover_para(cursor, not_before)
        while remaining > 0:
            _avancar_para_dia_util(cursor, member)
            hours = min(remaining, cursor.remaining_hours)
            used = cursor.daily_capacity - cursor.remaining_hours
            segments.append(AllocationSegment(cursor.date, hours,
                                              self._instante(cursor.date, used, cursor.daily_capacity),
                                              self._instante(cursor.date, used + hours, cursor.daily_capacity)))
            remaining -= hours
            cursor.remaining_hours -= hours
            if cursor.remaining_hours == 0:
                cursor.date += timedelta(days=1)
                cursor.remaining_hours = cursor.daily_capacity
        return segments

    def _adicionar_bloqueios(self, output: list[FlowBlockage], cursor: _Cursor, member: ForecastMember,
                             affected_task: ForecastTask, dependency_end: datetime, blocking_task_id: int | None,
                             cause: str) -> None:
        limit = _Cursor(cursor.date, cursor.daily_capacity, cursor.remaining_hours)
        self._mover_para(limit, dependency_end)
        current = cursor.date
        available = cursor.remaining_hours
        while current <= limit.date:
            blocked = available - (limit.remaining_hours if current == limit.date else 0)
            if _eh_dia_util(current, member) and blocked > 0:
                output.append(FlowBlockage(member.id, affected_task.id, blocking_task_id, current, blocked, cause))
            current += timedelta(days=1)
            available = member.daily_capacity_hours

    def _criar_cursor(self, entrada: ForecastInput, member: ForecastMember) -> _Cursor:
        cursor = _Cursor(entrada.forecast_date, member.daily_capacity_hours, member.daily_capacity_hours)
        self._mover_para(cursor, entrada.now or datetime.min)
        return cursor

    def _mover_para(self, cursor: _Cursor, instant: datetime) -> None:
        if instant <= self._instante_do_cursor(cursor):
            return
        moment = instant.time()
        if moment <= self._workday.workday_start:
            elapsed = Decimal(0)
        elif moment >= self._workday.workday_end:
            elapsed = self._duracao_do_expediente()
        else:
            elapsed = _horas(datetime.combine(date.min, moment) - datetime.combine(date.min, self._workday.workday_start))
        cursor.date = instant.date()
        cursor.remaining_hours = (cursor.daily_capacity * (1 - elapsed / self._duracao_do_expediente())).quantize(
            Decimal("0.0001"), ROUND_HALF_EVEN)
        if cursor.remaining_hours > 0:
            return
        cursor.date += timedelta(days=1)
        cursor.remaining_hours = cursor.daily_capacity

    def _instante_do_cursor(self, cursor: _Cursor) -> datetime:
        return self._instante(cursor.date, cursor.daily_capacity - cursor.remaining_hours, cursor.daily_capacity)

    def _instante(self, day: date, used_hours: Decimal, capacity: Decimal) -> datetime:
        minutes = (used_hours / capacity * self._duracao_do_expediente() * 60).quantize(Decimal(1), ROUND_HALF_EVEN)
        return datetime.combine(day, self._workday.workday_start) + timedelta(minutes=int(minutes))

    def _duracao_do_expediente(self) -> Decimal:
        return _horas(datetime.combine(date.min, self._workday.workday_end)
                      - datetime.combine(date.min, self._workday.workday_start))


def _validar_expediente(value: ForecastOptions) -> ForecastOptions:
    if value.workday_end <= value.workday_start:
        raise ValueError("O fim do expediente deve ser posterior ao início.")
    return value


def _obter_ultimo_termino(dependencies: list[ForecastTask],
                          forecasts: dict[int, TaskForecast]) -> tuple[datetime, int | None]:
    ends = [(forecasts[task.id].segments[-1].end, task.id) for task in dependencies
            if task.id in forecasts and forecasts[task.id].segments]
    return max(ends, key=lambda item: item[0], default=(datetime.min, None))


def _avancar_para_dia_util(cursor: _Cursor, member: ForecastMember) -> None:
    while not _eh_dia_util(cursor.date, member):
        cursor.date += timedelta(days=1)
        cursor.remaining_hours = cursor.daily_capacity


def _eh_dia_util(day: date, member: ForecastMember) -> bool:
    return day.weekday() < 5 and day not in (member.days_off or frozenset())


def _hora_local(value: datetime | None) -> datetime:
    if value is None:
        return datetime.min
    return value.astimezone().replace(tzinfo=None) if value.tzinfo else value


def _horas(value: timedelta) -> Decimal:
    return Decimal(value.days * 86400 + value.seconds) / 3600 + Decimal(value.microseconds) / 3_600_000_000


def _esta_em_andamento(task: ForecastTask) -> bool:
    return task.started_at is not None and task.state.lower() == "in progress"


def _esta_concluida(task: ForecastTask) -> bool:
    return task.state.lower() == "done"
