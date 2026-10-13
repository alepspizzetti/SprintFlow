# Arquitetura — SprintFlow (Gantt local da sprint lido do Azure DevOps)

Ferramenta local para a daily de uma squad: mostra USs e tasks na ordem do backlog, o histórico de estados e a previsão de término.

Este documento descreve **as partes do sistema, como os dados saem do Azure DevOps até a tela, as regras de ordenação e previsão, o cache local e os limites conhecidos**. Não contém segredos nem dados pessoais.

> **Legenda das cores (flowcharts)**
> 🟪 roxo = entrada / API · ⬜ cinza = externo / neutro · 🟨 amarelo = dados / decisão · 🟦 azul = processo / retorno · 🟥 vermelho = erro / parada
>
> **Legenda das setas (flowcharts)**
> `-->` sólida = ligação confirmada no código · `-.->` tracejada = ligação inferida (DI, interface, reflection, configuração ou convenção de nome)

O que foi verificado por leitura ou execução está na seção 9.

## 1. Visão geral

| Camada | O que faz | Acessa Azure DevOps? | Onde está |
| --- | --- | --- | --- |
| **Tela** | Timeline por US, barras das tasks, sync automático, preferências | ❌ não | `src/SprintFlow.Web/src/App.tsx`, `styles.css` |
| **API** | Rotas HTTP e arquivos estáticos do front | ❌ não | `src/SprintFlow.Api/Program.cs` |
| **Timeline** | Monta a resposta da tela a partir do plano | ❌ não | `Timeline/TimelineService.cs` |
| **Previsão** | Aloca horas por pessoa e calcula bloqueios | ❌ não | `Forecast/ForecastEngine.cs` |
| **Cache** | Memória + disco por sprint e catálogo | ❌ não | `AzureDevOps/MemorySprintForecastSource.cs` |
| **Fonte Azure** | Converte work items, membros, capacidade e revisões | ✅ via CLI | `AzureDevOps/AzureDevOpsSprintForecastSource.cs` |
| **Cliente CLI** | Executa `az` como processo e lê o JSON | ✅ sim | `AzureDevOps/AzureDevOpsCliClient.cs` |

```mermaid
flowchart LR
  classDef api fill:#EDE9FE,stroke:#7C3AED,color:#3B0764,stroke-width:2px
  classDef externo fill:#F3F4F6,stroke:#4B5563,color:#111827,stroke-width:2px
  classDef processo fill:#DBEAFE,stroke:#2563EB,color:#1E3A8A,stroke-width:2px
  classDef dados fill:#FEF3C7,stroke:#D97706,color:#78350F,stroke-width:2px

  usuario["👤 Usuário"]:::api
  subgraph WEB["src/SprintFlow.Web"]
    app["⚛️ App.tsx"]:::processo
    storage[("💾 localStorage")]:::dados
  end
  subgraph API["src/SprintFlow.Api"]
    rotas["🌐 Program.cs (rotas)"]:::api
    timeline["🔧 TimelineService"]:::processo
    engine["🔧 ForecastEngine"]:::processo
    cache["🗄️ MemorySprintForecastSource"]:::processo
    fonte["🔧 AzureDevOpsSprintForecastSource"]:::processo
    cli["🔧 AzureDevOpsCliClient"]:::processo
  end
  disco[("🗄️ ~/.local/share/SprintFlow/time")]:::dados
  az["🌐 az CLI"]:::externo
  ado[("🌐 Azure DevOps")]:::externo

  usuario --> app
  app --> storage
  app -->|HTTP /api| rotas
  rotas --> timeline
  timeline -.->|ISprintForecastSource via DI| cache
  timeline --> engine
  cache --> disco
  cache --> fonte
  fonte --> cli
  cli -->|processo| az
  az --> ado
```

O backend serve o build do front: `SprintFlow.Api.csproj` inclui `../SprintFlow.Web/dist/**/*` como `wwwroot`, e `Program.cs` usa `UseStaticFiles` e `MapFallbackToFile("index.html")`.

**Por que assim**

| Problema | Como o desenho resolve |
| --- | --- |
| O Azure DevOps é a fonte da verdade e não pode ser alterado | Só há leitura, sempre via `az` CLI. Não existe rota nem método de escrita. |
| Autenticação de cada usuário | Reaproveita a sessão do `az login`; o projeto não guarda credencial. |
| Carga completa no Azure leva dezenas de segundos | Cache em memória e disco; a tela abre com o último dado e sincroniza em segundo plano. |

## 2. Componentes

```mermaid
classDiagram
  direction LR
  class TimelineService {
    +ListarSprintsAsync()
    +ObterAsync(sprintId)
    +SincronizarAsync(sprintId)
  }
  class MemorySprintForecastSource {
    +ListarAsync()
    +ObterAsync(sprintId)
    +AtualizarAsync(sprintId)
    -BuscarSprintAsync()
    -LerAsync()
    -GravarAsync()
    -PreCarregarAsync()
  }
  class AzureDevOpsSprintForecastSource {
    +ListarAsync()
    +ObterAsync(sprintId)
    +MontarPeriodosDeAtividade(revisions)
  }
  class AzureDevOpsCliClient {
    +ObterIteracoesAsync()
    +ObterMembrosDoTimeAsync()
    +ObterCapacidadesAsync(iterationId)
    +ObterItensAsync(iterationPath)
    +ObterRevisoesDosItensAsync(ids)
    +DiagnosticarAsync()
  }
  class ForecastEngine {
    +CalcularPrevisao(input) ForecastResult
  }
  TimelineService --> MemorySprintForecastSource : ISprintForecastSource
  TimelineService --> ForecastEngine
  MemorySprintForecastSource --> AzureDevOpsSprintForecastSource
  AzureDevOpsSprintForecastSource --> AzureDevOpsCliClient
```

A ligação `TimelineService` → `MemorySprintForecastSource` é inferida pelo registro em `Program.cs` (`AddSingleton` de `ISprintForecastSource` para `MemorySprintForecastSource`). Todos os serviços são singletons.

| Arquivo | Responsabilidade |
| --- | --- |
| `src/SprintFlow.Api/Program.cs` | Configuração, DI e rotas `/api/*`; carrega `appsettings.Local.json` opcional. |
| `AzureDevOps/AzureDevOpsOptions.cs` | Chaves `AzureDevOps:Organization`, `Project`, `Team`, `ExecutablePath`, `CommandTimeoutSeconds`. |
| `AzureDevOps/AzureDevOpsCliClient.cs` | Executa `az`, lê os campos dos work items em lote via WIQL e aplica timeout e paralelismo de 6 leituras de revisões; contém `AzureDevOpsJsonParser`. |
| `AzureDevOps/AzureDevOpsSprintForecastSource.cs` | Classifica work items, normaliza estados, deriva papel e estágio, monta períodos de atividade. |
| `AzureDevOps/MemorySprintForecastSource.cs` | Cache em memória e disco, sync e aquecimento das sprints. |
| `Forecast/ForecastEngine.cs` | Previsão por capacidade e dependência entre estágios. |
| `Forecast/ForecastModels.cs` | Records de entrada e saída da previsão. |
| `Timeline/TimelineService.cs` | Monta `TimelineResponse` (pessoas, tasks, USs com seu estado em `State`, resumo). A tela exibe esse estado ao lado do ID da US. `TimelineStory` também recebe `Description` e `AzureUrl` para o modal da US. |
| `Timeline/TimelineContracts.cs` | Contratos da API e interfaces `ISprintForecastSource`, `ISprintForecastRefresher`. |
| `src/SprintFlow.Web/src/App.tsx` | Tela inteira: carga, sync automático, cálculo das barras e faixas, modal. |
| `src/SprintFlow.Web/src/styles.css` | Tema e layout da timeline. |
| `run.sh` / `stop.sh` | Build do front e do back, start em segundo plano e encerramento pelo PID. |

Antes do build, `run.sh` verifica o ASP.NET Core Runtime 10. Se estiver ausente no `dotnet` padrão e houver `pacman`, instala `aspnet-runtime-10.0` via `sudo pacman -S --needed` e verifica novamente. Senha e confirmação são solicitadas no terminal. Falha na instalação interrompe o script. Com `SPRINTFLOW_DOTNET_RUNTIME` definido ou sem `pacman`, o runtime precisa estar instalado previamente; a ausência após a verificação interrompe a execução com uma mensagem.

## 3. Fluxo principal: abrir a tela

```mermaid
sequenceDiagram
  autonumber
  participant T as App.tsx
  participant A as Program.cs
  participant S as TimelineService
  participant M as MemorySprintForecastSource
  participant D as Disco
  participant Z as AzureDevOpsSprintForecastSource
  T->>A: GET /api/settings
  T->>A: GET /api/sprints
  A->>M: ListarAsync
  alt catálogo em disco
    M->>D: sprints.json
    M-)Z: atualiza catálogo em segundo plano
  else sem cache
    M->>Z: ListarAsync
    M->>D: grava sprints.json
  end
  T->>A: GET /api/sprints/{id}/timeline
  A->>S: ObterAsync
  S->>M: ObterAsync
  alt em memória ou disco
    M-->>S: snapshot salvo
  else sem cache
    M->>Z: ObterAsync
    M->>D: grava sprint-{id}.json
  end
  S-->>T: TimelineResponse com syncedAt
  opt syncedAt anterior à abertura da página
    T->>A: POST /api/sprints/{id}/sync
    A->>S: SincronizarAsync
    S->>M: AtualizarAsync
    M->>Z: ObterAsync
    M->>D: regrava arquivo
    S-->>T: TimelineResponse nova
  end
```

- O sync automático roda uma vez por sprint em cada aba (`autoSynced` em `App.tsx`).
- A timeline dimensiona as colunas para cinco dias úteis na área visível, com mínimo de 110px por dia e rolagem horizontal. Os títulos das USs permanecem fixos. Ao abrir ou trocar de sprint, centraliza o momento atual se estiver dentro da sprint atual, limitado às bordas; as demais abrem no início. O sync preserva a posição escolhida pelo usuário.
- `TimelinePerson.DaysOff` expõe apenas as folgas individuais; `TimelineResponse.TeamDaysOff` expõe as coletivas, persistidas em `SprintForecastDataset.TeamDaysOff`. O sync consulta `teamdaysoff` via CLI em paralelo com a capacidade. A previsão combina as duas listas por pessoa, sem modificar o snapshot. Caches antigos sem folgas coletivas continuam legíveis e recebem esses dados no próximo sync. Datas de folga mantêm o dia calendário informado pelo Azure, sem conversão de fuso. A tela destaca a coluna coletiva em vermelho discreto, inclusive nas USs recolhidas, e hachura apenas o trecho da barra que coincide com a folga individual do responsável. A folga coletiva prevalece visualmente quando as duas coincidem.
- `ListarAsync` só retorna sprints com status `Current` ou `Future`.
- Ao carregar o catálogo, `PreCarregarAsync` chama `ObterAsync` para cada sprint, começando pela atual.

## 4. Fluxo de leitura do Azure DevOps

```mermaid
flowchart TD
  classDef processo fill:#DBEAFE,stroke:#2563EB,color:#1E3A8A,stroke-width:2px
  classDef decisao fill:#FEF3C7,stroke:#D97706,color:#78350F,stroke-width:2px
  classDef erro fill:#FEE2E2,stroke:#DC2626,color:#7F1D1D,stroke-width:2px
  classDef home fill:#DBEAFE,stroke:#2563EB,color:#1E3A8A,stroke-width:2px

  inicio["🔧 ObterAsync(sprintId)"]:::home
  iter["az boards iteration team list"]:::processo
  achou{"Iteração existe com caminho e datas?"}:::decisao
  falha["❌ AzureDevOpsReadException"]:::erro
  paralelo["Membros + capacidade + work items em paralelo"]:::processo
  wiql["az boards query: campos em lote por IterationPath ORDER BY StackRank"]:::processo
  atual{"Sprint atual?"}:::decisao
  rev["Revisões de cada task e bug"]:::processo
  monta["ForecastInput + URLs + atividade"]:::home

  inicio --> iter --> achou
  achou -->|não| falha
  achou -->|sim| paralelo
  paralelo --> wiql --> atual
  atual -->|sim| rev --> monta
  atual -->|não| monta
```

- Falha ao ler capacidade não interrompe a carga: `CarregarCapacidadesAsync` devolve lista vazia.
- Cada comando `az` tem timeout de `CommandTimeoutSeconds`, limitado entre 5 e 300 segundos.

## 5. Regras de negócio

| Tema | Regra |
| --- | --- |
| Tipos de item | `User Story` e `Product Backlog Item` são USs. `Task` e `Bug` são itens filhos. Outros tipos são ignorados. |
| Vínculo | Item filho sem `System.Parent` é descartado. Pai fora da sprint vira US placeholder com título `US #<id>`. |
| Ordem das USs | `Microsoft.VSTS.Common.StackRank`, fallback `BacklogPriority`, depois id. US sem ordem recebe a posição na lista ordenada. |
| Ordem das tasks | Dentro da US: ordem do Azure, depois id. As barras são empacotadas em faixas nessa ordem. |
| Estados | `new`/`to do`/`todo` → `Todo`; `active`/`in progress`/`doing` → `In Progress`; `resolved`/`review` → `Review`; `closed`/`done` → `Done`. |
| Esforço | `RemainingWork`, fallback `OriginalEstimate`, senão 0. |
| Papel da pessoa | Atividade de maior capacidade diária: contém `back` → Backend; `front` → Frontend; `qa`/`test` → QA; `pd`, `el`, `gpm`, `pm` exatos; senão PD. |
| Estágio da task | Título contém `front` ou ` ui` → Frontend; `qa`/`test` → QA; `back`/`api` → Backend; senão o papel do responsável; senão Other. |
| Ordem da previsão | Estágio (Backend, Frontend, QA, Other), depois ordem da US, ordem da task e id. |
| Dependências | Frontend começa no horário em que termina o último Backend aberto da mesma US. QA espera Backend e Frontend da US. Horas de espera por dia viram `FlowBlockage`. |
| Fila por pessoa | Uma task por vez. Task `In Progress` com início no histórico começa no primeiro `Active`, termina em agora + esforço restante e entra antes da fila, independentemente da ordem; várias em andamento da mesma pessoa ficam sobrepostas. Demais tasks seguem a ordem e começam no maior entre `System.CreatedDate`, agora e o fim da anterior. Sem histórico, `In Progress` segue a fila. |
| Alocação | Horas por dia até a capacidade da pessoa, distribuídas no expediente `Forecast:WorkdayStart`–`Forecast:WorkdayEnd` (padrão 08:00–18:00); cada segmento expõe `Start` e `End`; pula sábado, domingo, folgas individuais e folgas do time inteiro. Task com fim após o fim da sprint recebe `ExceedsSprint`. |
| Sem previsão | Task sem responsável da squad, sem esforço restante ou com capacidade 0 vai para `IgnoredTaskIds`. |
| Atividade | Só na sprint atual. Revisões em sequência com mesmo estado e responsável são unidas. `In Progress` → Active; `Review` → Review; `Done` → Done; outro estado → Assigned antes do início, Paused depois. |
| Dia de referência | Sprint atual: hoje, limitado ao intervalo da sprint. Outras sprints: data de início. |
| USs concluídas (métrica) | Estado da própria US no Azure igual a `Done`, independentemente do estado ou da existência de tasks. API fornece `EstaConcluida` para o contador e recolhimento na tela. |
| Barras | Coluna do dia = 24h; posição `(hora + minuto/60) / 24`. Início no primeiro `Active`; sem esse registro, usa o primeiro `Review` ou `Done`. Sem início registrado, usa o `Start` do primeiro segmento ou agora. Criação e atribuição ficam somente no histórico. Fim no `Done` ou agora, estendido até o `End` do último segmento previsto. |

Mensagem de erro que chega ao usuário, com dado fictício:

```text
A sprint solicitada não foi encontrada entre as 12 iterações disponíveis para o time configurado.
```

## 6. Dados

Não há banco. Os dados persistidos são arquivos JSON de cache e uma preferência no navegador.

```mermaid
erDiagram
  CATALOGO ||--o{ SNAPSHOT : "Id da sprint"
  CATALOGO {
    string Id "iteração do Azure"
    string Status "Current ou Future"
  }
  SNAPSHOT {
    object Sprint "SprintSummary"
    object Input "ForecastInput"
    map WorkItemUrls "id para URL"
    map Activity "id para períodos"
    datetime SyncedAt "momento do último fetch"
  }
```

- Local: `~/.local/share/SprintFlow/{Team}/` (`Environment.SpecialFolder.LocalApplicationData`). `sprints.json` guarda o catálogo; `sprint-<id>.json` guarda cada `SprintForecastDataset`.
- O nome da pasta é o `Team` com caracteres inválidos trocados por `_`.
- A previsão (`ForecastResult`) não é salva: é recalculada a cada requisição a partir do snapshot.
- `ForecastInput.ForecastDate` mantém o nome JSON `planningDate` para compatibilidade com os caches existentes.
- A descrição HTML (`System.Description`) segue do cliente CLI para `ForecastStory.Description`, cache e `TimelineStory.Description`; caches anteriores continuam válidos e recebem a descrição no próximo sync. A tela exibe o HTML em iframe sem permissões de sandbox, com scripts bloqueados por CSP.
- Um clique na US abre seus detalhes e tasks vinculadas; o clique é adiado por 350 ms para permitir que o duplo clique cancele o modal e alterne o recolhimento individual.
- Duplo clique em **USER STORY** recolhe todas ou expande TODO, IN PROGRESS e TESTING/TESTINGS quando a preferência estiver marcada; desmarcada, expande todas. A ação altera somente a expansão da tela, sem persistir uma nova preferência.
- Navegador: `localStorage['sprintflow.collapseDone']` guarda a preferência de recolher USs concluídas.

## 7. Cache, sync e retomada

| Situação | O que acontece |
| --- | --- |
| Primeira abertura sem cache | Carga completa no Azure; o resultado é salvo em disco. |
| Reinício do backend | Catálogo e sprints vêm do disco; o catálogo é atualizado em segundo plano. |
| Abrir ou trocar de sprint na tela | Mostra o snapshot e dispara sync em segundo plano se `syncedAt` for anterior à abertura da página. |
| Sync manual (**SYNC SPRINT**) | `AtualizarAsync` busca no Azure, troca a memória e regrava o arquivo. |
| Falha no sync | A tela mantém os dados anteriores e mostra o erro; o arquivo não é alterado. |
| Arquivo corrompido ou formato antigo | `LerAsync` registra aviso e trata como cache vazio. |
| Falha ao gravar | `GravarAsync` registra aviso; a resposta segue normalmente. |
| Concorrência | `SemaphoreSlim` por sprint e para o catálogo; escrita atômica com arquivo `.tmp` + `File.Move`. |

O catálogo carregado do disco não é substituído na tela até recarregar a página.

## 8. Rotas, erros e mensagens

| Rota | Método | Uso |
| --- | --- | --- |
| `/api/status` | GET | Diagnóstico: CLI, extensão, login e configuração (`DiagnosticarAsync`). |
| `/api/settings` | GET | Devolve `{ team }` para o topo da tela. |
| `/api/sprints` | GET | Catálogo; responde 503 se `AzureDevOps` não estiver configurado. |
| `/api/sprints/{sprintId}/timeline` | GET | Timeline da sprint (cache primeiro). |
| `/api/sprints/{sprintId}/sync` | POST | Força leitura no Azure e devolve a timeline nova. |

| Situação | Exceção | Mensagem |
| --- | --- | --- |
| `az` não instalado | `Win32Exception` tratada | `Azure CLI não encontrado.` |
| Timeout do comando | `OperationCanceledException` tratada | `A consulta excedeu o tempo limite.` |
| Comando `az` com erro | `AzureDevOpsReadException` | stderr do `az` |
| Sprint inexistente | `AzureDevOpsReadException` | `A sprint solicitada não foi encontrada…` |
| Falha ao carregar sprints (tela) | — | `Configure o time em appsettings.Local.json e verifique o Azure CLI.` |
| Falha no sync (tela) | — | `Sync falhou. Os dados anteriores continuam na tela.` |

## 9. Integração externa e autenticação

| Comando `az` | Uso | Evidência |
| --- | --- | --- |
| `boards iteration team list` | Sprints do time | código |
| `devops team list-member` | Membros | código |
| `devops invoke --area work --resource capacities` | Capacidade e folgas | código |
| `devops invoke --area work --resource teamdaysoff` | Folgas do time inteiro | código + execução |
| `boards query --wiql` | IDs e campos da sprint em lote, na ordem do backlog | código + execução |
| `devops invoke --area wit --resource revisions` | Histórico de estados | código |

Nenhum comando de escrita é usado. A credencial é a sessão do `az login`; o projeto não lê nem guarda token. `AZURE_CORE_ONLY_SHOW_ERRORS=true` é definido no processo filho.

A API só aceita os hosts `localhost` e `127.0.0.1` (`AllowedHosts` em `appsettings.json`). Requisições com outro `Host` recebem 400, o que impede um site externo de ler os dados via DNS rebinding.

## 10. CI e proteção da `master`

```mermaid
flowchart LR
  classDef api fill:#EDE9FE,stroke:#7C3AED,color:#3B0764,stroke-width:2px
  classDef processo fill:#DBEAFE,stroke:#2563EB,color:#1E3A8A,stroke-width:2px
  classDef decisao fill:#FEF3C7,stroke:#D97706,color:#78350F,stroke-width:2px
  classDef erro fill:#FEE2E2,stroke:#DC2626,color:#7F1D1D,stroke-width:2px

  pr["📥 Pull request para master"]:::api
  jobapi["🔧 Job API: dotnet build + test"]:::processo
  jobweb["🔧 Job Web: npm ci + build"]:::processo
  owner{"Aprovação do code owner?"}:::decisao
  merge["✅ Merge"]:::processo
  bloqueio["❌ Merge bloqueado"]:::erro

  pr --> jobapi
  pr --> jobweb
  jobapi --> owner
  jobweb --> owner
  owner -->|sim| merge
  owner -->|não| bloqueio
```

| Arquivo | Papel |
| --- | --- |
| `.github/workflows/ci.yml` | Jobs `API` e `Web` em PRs e pushes para `master`. |
| `.github/rulesets/master.json` | Bloqueia push direto, force-push e exclusão; exige PR, 1 aprovação do code owner e os checks `API` e `Web` com a branch atualizada. Administradores podem fazer bypass só via PR. |
| `.github/CODEOWNERS` | Define o aprovador obrigatório de todos os arquivos. |

O ruleset é aplicado no GitHub com `gh api` (ver `CONTRIBUTING.md`); o arquivo no repositório é a fonte, não a configuração ativa.

## 11. Limites conhecidos e verificação

| Ponto | Situação |
| --- | --- |
| Pausas | Só aparecem quando houve mudança de estado; períodos sem atualização no Azure não são detectados. |
| Histórico | Revisões só são lidas para a sprint atual. |
| Sprints passadas | Fora do catálogo (`Current` e `Future` apenas). |
| Estágio por título | Heurística por palavra; título ambíguo pode cair no estágio errado. |
| Catálogo do disco | Na virada da sprint, a primeira abertura pode marcar a sprint anterior como atual até recarregar a página. |
| Fuso | Datas de iteração usam UTC; horários das barras usam o fuso do navegador. |

| Fluxo ou afirmação | Fonte no código | Verificação executada | Limite |
| --- | --- | --- | --- |
| Build do front e do back | `run.sh` | `./run.sh` compilou sem erros (Vite + `dotnet build`). | Build não valida comportamento. |
| Cache em disco sobrevive a reinício | `MemorySprintForecastSource` | Timeline sem cache levou 34,2 s; após reinício, 0,05 s, com o mesmo `syncedAt`. | Medido numa sprint. |
| Sync com campos em lote | `AzureDevOpsCliClient.ObterItensAsync` | Sprint 34: 40,6 s antes e 21,5 s depois; resposta idêntica exceto `syncedAt`. | Uma medição por versão; histórico completo ainda é consultado. |
| Sync regrava arquivo | `AtualizarAsync`, `BuscarSprintAsync` | `POST /sync` atualizou `syncedAt` na resposta e no arquivo. | — |
| Bugs como filhos da US | `EhHistoria`, `EhTarefa` | Conferido via `/timeline` e `az boards work-item show` nos itens da sprint atual. | — |
| Rotas `/` e `/api/settings` | `Program.cs` | `curl` retornou `index.html` e `{ team }`. | — |
| Tela | `App.tsx` | Screenshot headless 1920×1080 com USs recolhidas e expandidas. | Clique, modal e popover não testados. |
| CI e ruleset | `.github/` | Primeiro run do CI passou (jobs `API` e `Web`). Ruleset `Proteger master` aplicado e ativo no GitHub. | Nomes dos checks dependem dos jobs `API` e `Web`. |
| Filtro de host | `appsettings.json` (`AllowedHosts`) | `curl` com `Host: evil.com` retornou 400; `127.0.0.1:5002` e `localhost:5002` retornaram 200. | — |
| Previsão e parser | `ForecastEngine`, `AzureDevOpsJsonParser` | `dotnet test` no CI: 10 testes, 10 aprovados. | Localmente o `dotnet test` não roda sem o ASP.NET Core 10 instalado. |

## 12. Instruções compartilhadas de IA

A estrutura de IA orienta o desenvolvimento; não participa da execução da API, da previsão ou do sync. Todos os recursos ficam em `.ai/`. O `AGENTS.md` da raiz é somente o índice de leitura compartilhado por Claude Code e Codex.

| Local | Responsabilidade | Carregamento |
| --- | --- | --- |
| `AGENTS.md` | Indicar o que ler sempre e por tarefa | Entrada nos clientes compatíveis |
| `.ai/rules/` | Orientações gerais, estilo, segurança, domínio, backend, frontend, testes, documentação e Git | Leitura conforme o índice |
| `.ai/workflows/` | Procedimentos de implementação, correção, revisão, desempenho e validação | Leitura conforme a tarefa ou skill |
| `.ai/skills/` | Skills de validação, revisão e documentação | Leitura pelo caminho indicado no índice ou no pedido |
| `.ai/agents/` | Perfis de revisor e investigador | Leitura pelo agente principal; não registra subagentes |
| `.ai/templates/` | Modelos de regra, workflow, perfil, plano, PR e decisão | Cópia e edição pelo usuário; não são políticas ativas |
| `.ai/evals/` | Cenários de avaliação do comportamento das instruções | Avaliação manual em ambiente isolado |
| `.ai/hooks/` | Guia para uma eventual automação por eventos | Nenhum hook ativado |
| `.ai/scripts/validate.py` | Conferência de referências e metadados das skills | Execução manual com Python 3.11+ |

```mermaid
flowchart LR
  classDef entrada fill:#EDE9FE,stroke:#7C3AED,color:#3B0764
  classDef dados fill:#FEF3C7,stroke:#D97706,color:#78350F
  classDef processo fill:#DBEAFE,stroke:#2563EB,color:#1E3A8A
  agents["AGENTS.md — índice"]:::entrada
  regras[".ai/rules"]:::dados
  workflows[".ai/workflows"]:::processo
  skills[".ai/skills"]:::dados
  perfis[".ai/agents"]:::dados
  agents -->|leitura obrigatória e por tarefa| regras
  agents -->|conforme pedido| workflows
  agents -->|conforme pedido| skills
  agents -->|conforme pedido| perfis
  skills -->|procedimentos| workflows
```

`.ai/` é uma convenção local; não há adaptadores nem links de descoberta nativa. Skills e perfis são documentos encaminhados pelo índice, sem comandos de skills ou registro automático de subagentes. O validador comprova integridade estrutural, não leitura nem obediência pelo cliente. Confira o carregamento do `AGENTS.md` e dos documentos pertinentes em uma sessão nova; Claude não estava instalado no ambiente de criação. Detalhes de uso e manutenção estão em `.ai/README.md`.

As funções próprias do backend, frontend, auxiliares e testes usam nomes em pt-BR. Identificadores de classes, campos JSON, rotas, arquivos e nomes exigidos por bibliotecas foram preservados. Renomeações foram verificadas pelos testes .NET e build do frontend.
