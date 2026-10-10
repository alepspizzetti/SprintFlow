#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
pid_file="$project_root/.run/backend.pid"
if [[ -f "$pid_file" ]]; then
  kill "$(<"$pid_file")" 2>/dev/null || true
  rm -f "$pid_file"
fi
echo "SprintFlow encerrado."
