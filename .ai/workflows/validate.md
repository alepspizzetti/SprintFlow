# Validar e executar o SprintFlow

Escolha os checks pelo que mudou; não execute toda a lista para qualquer tarefa.

| Mudança | Verificação |
| --- | --- |
| Backend, domínio ou contrato | `python3 -m unittest discover -s tests` |
| Frontend | `node --check web/app.js` e conferência do comportamento afetado |
| Estrutura compartilhada de IA | `python3 .ai/scripts/validate.py` |
| Script Bash | `bash -n caminho/do/script.sh` e cenários representativos |
| Qualquer diff | `git diff --check` |

## Reinício

Quando solicitado, ou quando necessário para servir código atualizado localmente:

```bash
./stop.sh && ./run.sh
curl --fail --silent http://127.0.0.1:5002/api/settings
```

`run.sh` exige Python 3.11+, inicia `python3 -m sprintflow` em segundo plano e escreve `.run/backend.log`. Para outro interpretador, use `SPRINTFLOW_PYTHON=/caminho/para/python3 ./run.sh`. Mudanças só em `web/` não exigem reinício; recarregue a página.

Para validar sync, confira rotas e configuração em `.docs/ARQUITETURA.md`; não faça fetch real se mocks ou cache responderem à verificação. Sync consulta Azure e regrava apenas o cache local.
