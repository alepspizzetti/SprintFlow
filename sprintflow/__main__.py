import os

from .server import executar

executar(os.environ.get("SPRINTFLOW_HOST", "127.0.0.1"), int(os.environ.get("SPRINTFLOW_PORT", "5002")))
