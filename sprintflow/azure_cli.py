from __future__ import annotations

import json
import os
import subprocess
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from datetime import datetime
from decimal import Decimal, InvalidOperation
from urllib.parse import quote

from .config import AzureDevOpsOptions

MAX_CONCURRENT_WORK_ITEM_READS = 6


class AzureDevOpsReadException(Exception):
    pass


@dataclass(frozen=True)
class AzureDevOpsDiagnostic:
    cli_available: bool
    extension_available: bool
    authenticated: bool
    configured: bool
    message: str


@dataclass(frozen=True)
class AzureWorkItem:
    id: int
    parent_id: int | None
    type: str
    title: str
    state: str
    assigned_to_id: str | None
    remaining_work: Decimal
    order: Decimal | None
    description: str | None = None
    created_at: datetime | None = None


@dataclass(frozen=True)
class AzureWorkItemRevision:
    state: str
    changed_at: datetime
    assigned_to_id: str | None
    assigned_to_name: str | None


class AzureDevOpsCliClient:
    def __init__(self, options: AzureDevOpsOptions) -> None:
        self.options = options

    def obter_url_do_item(self, work_item_id: int) -> str:
        return f"{self._obter_url_da_organizacao()}/{quote(self.options.project, safe='')}/_workitems/edit/{work_item_id}"

    def diagnosticar(self) -> AzureDevOpsDiagnostic:
        configured = self.options.is_configured
        if not self._tentar_executar(["version", "--output", "json"])[0]:
            return AzureDevOpsDiagnostic(False, False, False, configured, "Azure CLI não foi encontrado.")
        if not self._tentar_executar(["extension", "show", "--name", "azure-devops", "--output", "json"])[0]:
            return AzureDevOpsDiagnostic(True, False, False, configured, "Extensão azure-devops não está instalada.")
        authenticated = self._tentar_executar(["account", "show", "--output", "json"])[0]
        message = ("Informe organização, projeto e time." if not configured
                   else "Pronto para consulta." if authenticated else "Faça login no Azure CLI.")
        return AzureDevOpsDiagnostic(True, True, authenticated, configured, message)

    def obter_iteracoes(self) -> list[dict]:
        return ler_lista(self._executar(["boards", "iteration", "team", "list", "--organization", self._obter_url_da_organizacao(),
                                         "--project", self.options.project, "--team", self.options.team, "--output", "json"]))

    def obter_membros_do_time(self) -> list[dict]:
        return ler_lista(self._executar(["devops", "team", "list-member", "--organization", self._obter_url_da_organizacao(),
                                         "--project", self.options.project, "--team", self.options.team, "--output", "json"]))

    def obter_capacidades(self, iteration_id: str) -> list[dict]:
        return ler_lista(self._executar(["devops", "invoke", "--organization", self._obter_url_da_organizacao(),
                                         "--area", "work", "--resource", "capacities", "--route-parameters",
                                         f"project={self.options.project}", f"team={self.options.team}",
                                         f"iterationId={iteration_id}", "--api-version", "7.1", "--output", "json"]))

    def obter_folgas_do_time(self, iteration_id: str) -> list[dict]:
        return ler_lista(self._executar(["devops", "invoke", "--organization", self._obter_url_da_organizacao(),
                                         "--area", "work", "--resource", "teamdaysoff", "--route-parameters",
                                         f"project={self.options.project}", f"team={self.options.team}",
                                         f"iterationId={iteration_id}", "--api-version", "7.1", "--http-method", "GET",
                                         "--output", "json"]))

    def obter_itens(self, iteration_path: str) -> list[AzureWorkItem]:
        safe_path = iteration_path.replace("'", "''")
        wiql = ("SELECT [System.Id], [System.Parent], [System.WorkItemType], [System.Title], [System.Description], "
                "[System.State], [System.AssignedTo], [System.CreatedDate], [Microsoft.VSTS.Scheduling.RemainingWork], "
                "[Microsoft.VSTS.Scheduling.OriginalEstimate], [Microsoft.VSTS.Common.StackRank], "
                "[Microsoft.VSTS.Common.BacklogPriority] FROM WorkItems "
                f"WHERE [System.IterationPath] = '{safe_path}' ORDER BY [Microsoft.VSTS.Common.StackRank]")
        return ler_itens(self._executar(["boards", "query", "--organization", self._obter_url_da_organizacao(),
                                         "--wiql", wiql, "--output", "json"]))

    def obter_revisoes_dos_itens(self, work_item_ids: list[int]) -> dict[int, list[AzureWorkItemRevision]]:
        ids = list(dict.fromkeys(work_item_ids))
        with ThreadPoolExecutor(max_workers=MAX_CONCURRENT_WORK_ITEM_READS) as executor:
            results = list(executor.map(self._obter_revisoes, ids))
        return dict(zip(ids, results))

    def _obter_revisoes(self, work_item_id: int) -> list[AzureWorkItemRevision]:
        return ler_revisoes(self._executar(["devops", "invoke", "--organization", self._obter_url_da_organizacao(),
                                            "--area", "wit", "--resource", "revisions", "--route-parameters",
                                            f"id={work_item_id}", "--api-version", "7.1", "--output", "json"]))

    def _obter_url_da_organizacao(self) -> str:
        organization = self.options.organization
        if organization.lower().startswith("http"):
            return organization.rstrip("/")
        return f"https://dev.azure.com/{organization.strip('/')}"

    def _executar(self, args: list[str]) -> str:
        success, output, error = self._tentar_executar(args, min(max(self.options.command_timeout_seconds, 5), 300))
        if not success:
            raise AzureDevOpsReadException(error)
        return output

    def _tentar_executar(self, args: list[str], timeout: int | None = None) -> tuple[bool, str, str]:
        try:
            process = subprocess.run([self.options.executable_path, *args], capture_output=True, text=True,
                                     encoding="utf-8", timeout=timeout,
                                     env={**os.environ, "AZURE_CORE_ONLY_SHOW_ERRORS": "true"})
        except subprocess.TimeoutExpired:
            return False, "", "A consulta excedeu o tempo limite."
        except OSError:
            return False, "", "Azure CLI não encontrado."
        return process.returncode == 0, process.stdout, process.stderr.strip()


def ler_lista(text: str) -> list[dict]:
    root = json.loads(text)
    if isinstance(root, list):
        return root
    for key in ("value", "workItems", "daysOff"):
        if key in root:
            return root[key] or []
    return root["teamMembers"] or []


def ler_itens(text: str) -> list[AzureWorkItem]:
    if not text.strip():
        return []
    root = json.loads(text)
    return [] if root is None else [_ler_item(item) for item in root]


def _ler_item(item: dict) -> AzureWorkItem:
    fields = item["fields"]
    assigned = fields.get("System.AssignedTo")
    return AzureWorkItem(
        _inteiro(fields, "System.Id") or 0,
        _inteiro(fields, "System.Parent"),
        _texto(fields, "System.WorkItemType") or "Unknown",
        _texto(fields, "System.Title") or "",
        _texto(fields, "System.State") or "Unknown",
        assigned.get("id") if isinstance(assigned, dict) else None,
        _primeiro_numero(fields, "Microsoft.VSTS.Scheduling.RemainingWork",
                         "Microsoft.VSTS.Scheduling.OriginalEstimate") or Decimal(0),
        _primeiro_numero(fields, "Microsoft.VSTS.Common.StackRank", "Microsoft.VSTS.Common.BacklogPriority"),
        _texto(fields, "System.Description"),
        ler_data_hora(_texto(fields, "System.CreatedDate")))


def ler_revisoes(text: str) -> list[AzureWorkItemRevision]:
    root = json.loads(text)
    values = root if isinstance(root, list) else root["value"]
    revisions = []
    for item in values:
        fields = item.get("fields") or {}
        changed_at = ler_data_hora(fields.get("System.ChangedDate"))
        if "System.State" not in fields or changed_at is None:
            continue
        assigned = fields.get("System.AssignedTo")
        member_id = assigned.get("id") if isinstance(assigned, dict) else None
        member_name = assigned.get("displayName") if isinstance(assigned, dict) else assigned if isinstance(assigned, str) else None
        revisions.append(AzureWorkItemRevision(fields["System.State"] or "", changed_at, member_id, member_name))
    return revisions


def ler_data_hora(value: object) -> datetime | None:
    if not isinstance(value, str):
        return None
    try:
        return datetime.fromisoformat(value)
    except ValueError:
        return None


def _texto(fields: dict, name: str) -> str | None:
    if name not in fields:
        return None
    value = fields[name]
    return value if isinstance(value, str) else json.dumps(value)


def _numero(fields: dict, name: str) -> Decimal | None:
    value = fields.get(name)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    try:
        return Decimal(str(value))
    except InvalidOperation:
        return None


def _primeiro_numero(fields: dict, *names: str) -> Decimal | None:
    return next((value for value in (_numero(fields, name) for name in names) if value is not None), None)


def _inteiro(fields: dict, name: str) -> int | None:
    value = fields.get(name)
    return value if isinstance(value, int) and not isinstance(value, bool) else None
