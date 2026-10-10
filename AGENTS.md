# AGENTS.md

Guia para agentes de código neste repositório. Leia também o `README.md` e a arquitetura em `.docs/ARQUITETURA.md`.

## Visão geral

- `src/SprintFlow.Api`: backend ASP.NET Core (.NET 10, minimal APIs em `Program.cs`).
  - `AzureDevOps/AzureDevOpsCliClient.cs`: única porta para o Azure DevOps, sempre via `az` CLI. Não use chamadas HTTP diretas.
  - `AzureDevOps/AzureDevOpsSprintPlanningSource.cs`: converte work items em `PlanningInput` (USs, tasks, membros, capacidade) e monta o histórico de atividade a partir das revisões.
  - `AzureDevOps/MemorySprintPlanningSource.cs`: cache em memória + disco (`~/.local/share/SprintFlow/<time>/`). Ordem de leitura: memória, disco, Azure. Todo fetch no Azure regrava o arquivo com escrita atômica.
  - `Planning/ObjectivePlanningEngine.cs`: previsão de alocação, término e bloqueios.
  - `Timeline/`: contratos da API e `TimelineService`, que monta a resposta da tela.
- `src/SprintFlow.Web`: React + Vite + TypeScript, tudo em `src/App.tsx` e `src/styles.css`.
- `tests/SprintFlow.Tests`: xUnit.

## Regras de domínio

- O Azure DevOps é somente leitura. Nunca escreva no Azure.
- A ordem exibida segue o backlog do Azure: `Microsoft.VSTS.Common.StackRank`, com fallback para `BacklogPriority`. USs por ordem; tasks dentro da US por ordem e depois por id.
- `User Story` e `Product Backlog Item` são USs. `Task` e `Bug` são itens filhos da US (`System.Parent`). Itens sem pai são ignorados.
- Estados são normalizados para `Todo`, `In Progress`, `Review` e `Done`.
- O histórico de atividade só é carregado para a sprint atual.

## Frontend

- Timeline por US: cada coluna de dia representa 24h; barras são posicionadas por `(hora + minuto/60) / 24`.
- Barras empacotadas em faixas na ordem do DevOps. Barra: primeira linha `#id título`, segunda linha o responsável.
- Preferência "recolher USs concluídas" fica em `localStorage` (`sprintflow.collapseDone`).
- Ao abrir uma sprint, a tela mostra o cache e dispara um sync em segundo plano se o dado for anterior à abertura da página.

## Convenções

- Não adicione comentários no código.
- Mantenha o menor diff que cumpra o pedido; não misture refactors.
- Combine com o usuário antes de mudanças de design ou de comportamento fora do pedido.
- Textos de interface e mensagens em português.
- Qualquer alteração na estrutura do projeto (pastas, projetos, camadas, componentes, rotas, fluxos, cache, integrações ou scripts `run.sh`/`stop.sh`) deve atualizar `.docs/ARQUITETURA.md` na mesma mudança, junto com este arquivo e o `README.md` quando forem afetados.

## Repositório

- A `master` é protegida pelo ruleset `.github/rulesets/master.json`: só PR, 1 aprovação do code owner e checks `API` e `Web` do `.github/workflows/ci.yml`.
- Se renomear os jobs do CI, atualize os `context` do ruleset.

## Comandos

```bash
./run.sh                                   # build front + back e sobe em http://127.0.0.1:5002
./stop.sh                                  # encerra
dotnet test tests/SprintFlow.Tests         # testes
cd src/SprintFlow.Web && npm run build    # build do front
```

Se o `run.sh` acusar falta do ASP.NET Core 10, use `SPRINTFLOW_DOTNET_RUNTIME=/caminho/para/dotnet ./run.sh`. Depois de alterar o frontend, rode `./stop.sh && ./run.sh` para o backend servir o build novo.
