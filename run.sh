#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
runtime_dir="$project_root/.run"
runtime_bin="${SPRINTFLOW_DOTNET_RUNTIME:-dotnet}"
mkdir -p "$runtime_dir/dotnet-home"

if ! "$runtime_bin" --list-runtimes | grep -q '^Microsoft.AspNetCore.App 10\.'; then
  echo "Instale o runtime ASP.NET Core 10 para executar o SprintFlow." >&2
  exit 1
fi

if [[ -f "$runtime_dir/backend.pid" ]] && kill -0 "$(<"$runtime_dir/backend.pid")" 2>/dev/null; then
  echo "SprintFlow já está em execução: http://127.0.0.1:5002"
  exit 0
fi

if [[ ! -d "$project_root/src/SprintFlow.Web/node_modules" ]]; then
  npm --prefix "$project_root/src/SprintFlow.Web" ci
fi
npm --prefix "$project_root/src/SprintFlow.Web" run build
DOTNET_CLI_HOME="$runtime_dir/dotnet-home" dotnet build "$project_root/src/SprintFlow.Api/SprintFlow.Api.csproj" -m:1 -p:AllowMissingPrunePackageData=true

nohup setsid env DOTNET_CLI_HOME="$runtime_dir/dotnet-home" ASPNETCORE_URLS="http://127.0.0.1:5002" \
  "$runtime_bin" "$project_root/src/SprintFlow.Api/bin/Debug/net10.0/SprintFlow.Api.dll" \
  >"$runtime_dir/backend.log" 2>&1 < /dev/null &
echo "$!" >"$runtime_dir/backend.pid"
sleep 1
if ! kill -0 "$(<"$runtime_dir/backend.pid")" 2>/dev/null; then
  cat "$runtime_dir/backend.log" >&2
  exit 1
fi
echo "SprintFlow: http://127.0.0.1:5002"
