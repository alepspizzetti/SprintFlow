from __future__ import annotations

import json
import logging
import mimetypes
import re
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit

from .azure_cli import AzureDevOpsCliClient, AzureDevOpsReadException
from .azure_source import AzureDevOpsSprintForecastSource
from .cache import MemorySprintForecastSource
from .config import PROJECT_ROOT, Settings, carregar_configuracao
from .forecast import ForecastEngine
from .models import para_json
from .timeline import TimelineService

logger = logging.getLogger(__name__)
WEB_ROOT = PROJECT_ROOT / "web"
_ROTA_DA_SPRINT = re.compile(r"^/api/sprints/([^/]+)/(timeline|sync)$")


class Aplicacao:
    def __init__(self, settings: Settings) -> None:
        self.settings = settings
        self.client = AzureDevOpsCliClient(settings.azure)
        source = MemorySprintForecastSource(AzureDevOpsSprintForecastSource(self.client), settings.azure.team)
        self.timeline = TimelineService(source, ForecastEngine(settings.forecast))


def criar_handler(aplicacao: Aplicacao) -> type[BaseHTTPRequestHandler]:
    class Handler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            self._atender("GET")

        def do_POST(self) -> None:
            self._atender("POST")

        def log_message(self, format: str, *args: object) -> None:
            logger.info("%s %s", self.address_string(), format % args)

        def _atender(self, method: str) -> None:
            if not self._host_permitido():
                self._responder_texto(HTTPStatus.BAD_REQUEST, "Host não permitido.")
                return
            path = unquote(urlsplit(self.path).path)
            try:
                if path.startswith("/api/"):
                    self._atender_api(method, path)
                elif method == "GET":
                    self._servir_arquivo(path)
                else:
                    self._responder_texto(HTTPStatus.METHOD_NOT_ALLOWED, "Método não permitido.")
            except AzureDevOpsReadException as error:
                logger.error("Falha ao consultar o Azure DevOps: %s", error)
                self._responder_problema(HTTPStatus.INTERNAL_SERVER_ERROR, str(error))
            except Exception:
                logger.exception("Erro inesperado em %s %s", method, path)
                self._responder_problema(HTTPStatus.INTERNAL_SERVER_ERROR, "Erro inesperado no servidor.")

        def _atender_api(self, method: str, path: str) -> None:
            if method == "GET" and path == "/api/status":
                self._responder_json(aplicacao.client.diagnosticar())
            elif method == "GET" and path == "/api/settings":
                self._responder_json({"team": aplicacao.settings.azure.team})
            elif method == "GET" and path == "/api/sprints":
                if not aplicacao.settings.azure.is_configured:
                    self._responder_problema(HTTPStatus.SERVICE_UNAVAILABLE, "Configure AzureDevOps em appsettings.Local.json.")
                else:
                    self._responder_json(aplicacao.timeline.listar_sprints())
            elif (match := _ROTA_DA_SPRINT.match(path)) and (method, match[2]) in (("GET", "timeline"), ("POST", "sync")):
                timeline = (aplicacao.timeline.obter if match[2] == "timeline" else aplicacao.timeline.sincronizar)(match[1])
                if timeline is None:
                    self._responder_texto(HTTPStatus.NOT_FOUND, "Sprint não encontrada.")
                else:
                    self._responder_json(timeline)
            else:
                self._responder_texto(HTTPStatus.NOT_FOUND, "Rota não encontrada.")

        def _servir_arquivo(self, path: str) -> None:
            target = (WEB_ROOT / path.lstrip("/")).resolve()
            if not target.is_relative_to(WEB_ROOT.resolve()) or not target.is_file():
                target = WEB_ROOT / "index.html"
            content_type = mimetypes.guess_type(target.name)[0] or "application/octet-stream"
            if target.suffix == ".js":
                content_type = "text/javascript"
            self._responder(HTTPStatus.OK, target.read_bytes(), content_type + "; charset=utf-8")

        def _host_permitido(self) -> bool:
            host = (self.headers.get("Host") or "").strip().lower()
            hostname = host[1:host.index("]")] if host.startswith("[") else host.rsplit(":", 1)[0]
            allowed = aplicacao.settings.allowed_hosts
            return "*" in allowed or hostname in allowed

        def _responder_json(self, value: object, status: HTTPStatus = HTTPStatus.OK) -> None:
            body = json.dumps(para_json(value), ensure_ascii=False).encode("utf-8")
            self._responder(status, body, "application/json; charset=utf-8")

        def _responder_problema(self, status: HTTPStatus, detail: str) -> None:
            body = json.dumps({"title": status.phrase, "status": int(status), "detail": detail}, ensure_ascii=False)
            self._responder(status, body.encode("utf-8"), "application/problem+json; charset=utf-8")

        def _responder_texto(self, status: HTTPStatus, text: str) -> None:
            self._responder(status, text.encode("utf-8"), "text/plain; charset=utf-8")

        def _responder(self, status: HTTPStatus, body: bytes, content_type: str) -> None:
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-cache")
            self.end_headers()
            self.wfile.write(body)

    return Handler


def executar(host: str = "127.0.0.1", port: int = 5002, root: Path = PROJECT_ROOT) -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    server = ThreadingHTTPServer((host, port), criar_handler(Aplicacao(carregar_configuracao(root))))
    logger.info("SprintFlow em http://%s:%s", host, port)
    server.serve_forever()
