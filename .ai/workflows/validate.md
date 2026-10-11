# Validar e executar o SprintFlow

Escolha os checks pelo que mudou; não execute toda a lista para qualquer tarefa.

| Mudança | Verificação |
| --- | --- |
| Backend, domínio ou contrato | `dotnet test tests/SprintFlow.Tests` |
| Frontend | `npm --prefix src/SprintFlow.Web run build` e conferência do comportamento afetado |
| Estrutura compartilhada de IA | `python3 .ai/scripts/validate.py` |
| Script Bash | `bash -n caminho/do/script.sh` e cenários representativos |
| Qualquer diff | `git diff --check` |

## Build e reinício

Quando solicitado, ou quando necessário para servir código atualizado localmente:

```bash
./stop.sh && ./run.sh
curl --fail --silent http://127.0.0.1:5002/api/settings
```

`run.sh` compila front e back, inicia em segundo plano e escreve `.run/backend.log`. No CachyOS/Arch, instala o ASP.NET Core 10 via `sudo pacman` se ausente no `dotnet` padrão; pode pedir senha e confirmação. Para runtime externo, use `SPRINTFLOW_DOTNET_RUNTIME=/caminho/para/dotnet ./run.sh`, sem instalação automática.

Para validar sync, confira rotas e configuração em `.docs/ARQUITETURA.md`; não faça fetch real se mocks ou cache responderem à verificação. Sync consulta Azure e regrava apenas o cache local.
