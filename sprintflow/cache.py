from __future__ import annotations

import json
import logging
import os
import re
import threading
from dataclasses import replace
from datetime import datetime
from pathlib import Path
from typing import Any, Callable, TypeVar

from .azure_source import AzureDevOpsSprintForecastSource
from .models import SprintForecastDataset, SprintSummary, ler_dataset, ler_sprint, para_json

logger = logging.getLogger(__name__)
T = TypeVar("T")


class MemorySprintForecastSource:
    def __init__(self, azure: AzureDevOpsSprintForecastSource, team: str, cache_root: Path | None = None) -> None:
        self._azure = azure
        self._snapshots: dict[str, SprintForecastDataset] = {}
        self._sprint_locks: dict[str, threading.Lock] = {}
        self._locks_guard = threading.Lock()
        self._catalog_lock = threading.Lock()
        self._catalog: list[SprintSummary] | None = None
        base = cache_root or Path(os.environ.get("XDG_DATA_HOME") or Path.home() / ".local" / "share") / "SprintFlow"
        self._cache_directory = base / re.sub(r'[\x00-\x1f/]', "_", team)

    def listar(self) -> list[SprintSummary]:
        if self._catalog is not None:
            return self._catalog
        with self._catalog_lock:
            if self._catalog is None:
                cached = self._ler(self._catalog_path, lambda data: [ler_sprint(item) for item in data])
                if cached is None:
                    self._catalog = self._buscar_catalogo()
                else:
                    self._catalog = cached
                    self._em_segundo_plano(self._atualizar_catalogo)
                self._em_segundo_plano(self._pre_carregar, list(self._catalog))
            return self._catalog

    def obter(self, sprint_id: str) -> SprintForecastDataset | None:
        snapshot = self._snapshots.get(sprint_id)
        if snapshot is not None:
            return snapshot
        with self._obter_trava(sprint_id):
            snapshot = self._snapshots.get(sprint_id)
            if snapshot is not None:
                return snapshot
            snapshot = self._ler(self._caminho_da_sprint(sprint_id), ler_dataset) or self._buscar_sprint(sprint_id)
            if snapshot is not None:
                self._snapshots[sprint_id] = snapshot
            return snapshot

    def atualizar(self, sprint_id: str) -> bool:
        with self._obter_trava(sprint_id):
            fresh = self._buscar_sprint(sprint_id)
            if fresh is None:
                return False
            self._snapshots[sprint_id] = fresh
            return True

    @property
    def _catalog_path(self) -> Path:
        return self._cache_directory / "sprints.json"

    def _caminho_da_sprint(self, sprint_id: str) -> Path:
        return self._cache_directory / f"sprint-{sprint_id}.json"

    def _obter_trava(self, sprint_id: str) -> threading.Lock:
        with self._locks_guard:
            return self._sprint_locks.setdefault(sprint_id, threading.Lock())

    def _buscar_catalogo(self) -> list[SprintSummary]:
        fresh = [sprint for sprint in self._azure.listar() if sprint.status in ("Current", "Future")]
        self._gravar(self._catalog_path, fresh)
        return fresh

    def _atualizar_catalogo(self) -> None:
        try:
            self._catalog = self._buscar_catalogo()
        except Exception:
            logger.warning("Falha ao atualizar o catálogo de sprints em segundo plano.", exc_info=True)

    def _buscar_sprint(self, sprint_id: str) -> SprintForecastDataset | None:
        fresh = self._azure.obter(sprint_id)
        if fresh is None:
            return None
        fresh = replace(fresh, synced_at=datetime.now().astimezone())
        self._gravar(self._caminho_da_sprint(sprint_id), fresh)
        return fresh

    def _pre_carregar(self, sprints: list[SprintSummary]) -> None:
        for sprint in sorted(sprints, key=lambda item: (0 if item.status == "Current" else 1, item.start_date)):
            try:
                self.obter(sprint.id)
            except Exception:
                logger.info("Pré-carga da sprint %s falhou; a tela ou o sync manual tentam de novo.", sprint.id, exc_info=True)

    def _ler(self, path: Path, converter: Callable[[Any], T]) -> T | None:
        if not path.exists():
            return None
        try:
            return converter(json.loads(path.read_text(encoding="utf-8")))
        except (OSError, ValueError, KeyError, TypeError):
            logger.warning("Cache local ignorado: %s", path, exc_info=True)
            return None

    def _gravar(self, path: Path, value: object) -> None:
        try:
            self._cache_directory.mkdir(parents=True, exist_ok=True)
            temporary = path.with_name(path.name + ".tmp")
            temporary.write_text(json.dumps(para_json(value, enums_by_name=False), ensure_ascii=False), encoding="utf-8")
            temporary.replace(path)
        except OSError:
            logger.warning("Não foi possível gravar o cache local: %s", path, exc_info=True)

    @staticmethod
    def _em_segundo_plano(target, *args) -> None:
        threading.Thread(target=target, args=args, daemon=True).start()
