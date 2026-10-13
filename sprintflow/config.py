from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import time
from pathlib import Path

from .models import ForecastOptions

PROJECT_ROOT = Path(__file__).resolve().parent.parent


@dataclass(frozen=True)
class AzureDevOpsOptions:
    executable_path: str = "az"
    organization: str = ""
    project: str = ""
    team: str = ""
    command_timeout_seconds: int = 60

    @property
    def is_configured(self) -> bool:
        return bool(self.organization.strip() and self.project.strip() and self.team.strip())


@dataclass(frozen=True)
class Settings:
    allowed_hosts: tuple[str, ...]
    azure: AzureDevOpsOptions
    forecast: ForecastOptions


def carregar_configuracao(root: Path = PROJECT_ROOT) -> Settings:
    data = _mesclar(_ler(root / "appsettings.json"), _ler(root / "appsettings.Local.json"))
    azure = data.get("AzureDevOps") or {}
    forecast = data.get("Forecast") or {}
    return Settings(
        tuple(host.strip().lower() for host in str(data.get("AllowedHosts") or "localhost").split(";") if host.strip()),
        AzureDevOpsOptions(
            executable_path=azure.get("ExecutablePath") or "az",
            organization=azure.get("Organization") or "",
            project=azure.get("Project") or "",
            team=azure.get("Team") or "",
            command_timeout_seconds=int(azure.get("CommandTimeoutSeconds") or 60)),
        ForecastOptions(
            workday_start=time.fromisoformat(forecast.get("WorkdayStart") or "08:00"),
            workday_end=time.fromisoformat(forecast.get("WorkdayEnd") or "18:00")))


def _ler(path: Path) -> dict:
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def _mesclar(base: dict, override: dict) -> dict:
    result = dict(base)
    for key, value in override.items():
        result[key] = _mesclar(result[key], value) if isinstance(value, dict) and isinstance(result.get(key), dict) else value
    return result
