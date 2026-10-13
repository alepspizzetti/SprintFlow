#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
runtime_dir="$project_root/.run"
python_bin="${SPRINTFLOW_PYTHON:-python3}"
mkdir -p "$runtime_dir"

if ! "$python_bin" -c 'import sys; sys.exit(sys.version_info < (3, 11))' 2>/dev/null; then
  echo "Instale o Python 3.11 ou superior para executar o SprintFlow ou informe o executável em SPRINTFLOW_PYTHON." >&2
  exit 1
fi

if [[ -f "$runtime_dir/backend.pid" ]] && kill -0 "$(<"$runtime_dir/backend.pid")" 2>/dev/null; then
  echo "SprintFlow já está em execução: http://127.0.0.1:5002"
  exit 0
fi

cd "$project_root"
nohup setsid "$python_bin" -m sprintflow >"$runtime_dir/backend.log" 2>&1 < /dev/null &
echo "$!" >"$runtime_dir/backend.pid"
sleep 1
if ! kill -0 "$(<"$runtime_dir/backend.pid")" 2>/dev/null; then
  cat "$runtime_dir/backend.log" >&2
  exit 1
fi
echo "SprintFlow: http://127.0.0.1:5002"
