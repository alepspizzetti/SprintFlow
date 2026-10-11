# SprintFlow

Gantt local para a daily de uma squad. Mostra as USs e tasks da sprint na ordem do backlog do Azure DevOps, com o histórico de estados de cada task e a previsão de término calculada a partir de capacidade, esforço restante e dependências. O Azure DevOps é a fonte da verdade: o aplicativo só lê dados.

## Estrutura

```text
src/
  SprintFlow.Api/      backend ASP.NET Core: API, cache, leitura do Azure e previsão
    Forecast/         motor e modelos da previsão de execução
  SprintFlow.Web/      frontend React + Vite, servido pelo backend
tests/
  SprintFlow.Tests/    testes xUnit
.docs/
  ARQUITETURA.md       arquitetura, fluxos, regras e limites
.ai/                  regras, workflows, skills, perfis e modelos compartilhados de IA
AGENTS.md             ponto de entrada das instruções compartilhadas
.github/
  workflows/ci.yml     CI: build e testes da API e build do Web
  rulesets/master.json proteção da branch master
  CODEOWNERS           aprovador obrigatório dos PRs
run.sh / stop.sh       sobe e encerra o aplicativo
```

Detalhes de arquitetura em [`.docs/ARQUITETURA.md`](.docs/ARQUITETURA.md). Para contribuir, veja [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Desenvolvimento com IA

Comece por [`AGENTS.md`](AGENTS.md). Ele indica quando ler os arquivos em `.ai/`, compartilhados por Claude Code e Codex.

O [guia `.ai/README.md`](.ai/README.md) explica a organização e os modelos editáveis. Regras, workflows, skills e perfis ficam exclusivamente em `.ai/` e são lidos conforme o índice do `AGENTS.md`. Não há registro nativo de skills ou subagentes, hooks ativos, servidor MCP ou política global configurada.

Funções e métodos próprios devem ter nomes em português do Brasil, sem acentos nos identificadores; nomes obrigatórios de frameworks e contratos externos são preservados. As convenções completas estão em [`.ai/rules/code-style.md`](.ai/rules/code-style.md).

Para validar referências e metadados das skills:

```bash
python3 .ai/scripts/validate.py
```

Abra uma nova sessão para conferir a leitura do `AGENTS.md` e dos arquivos pertinentes em `.ai/`. Skills não ficam disponíveis como comandos nativos; para solicitar uma, indique seu caminho ou o tipo de tarefa listado no índice.

## Dependências

| Dependência | Versão | Uso |
| --- | --- | --- |
| .NET SDK | 10 | Build do backend e testes |
| ASP.NET Core Runtime | 10 | Execução do backend |
| Node.js + npm | Node 20+ | Build do frontend (React + Vite) |
| Azure CLI | 2.x | Leitura do Azure DevOps |
| Extensão `azure-devops` do Azure CLI | 1.x | Comandos `az boards` |
| Chromium/Chrome/Firefox | atual | Abrir a tela |

### Instalação no CachyOS/Arch

```bash
sudo pacman -S dotnet-sdk-10.0 aspnet-runtime-10.0 nodejs npm azure-cli
az extension add --name azure-devops
az login
```

Em outras distribuições, instale o .NET 10 SDK e o ASP.NET Core Runtime 10 pelo [site da Microsoft](https://dotnet.microsoft.com/download/dotnet/10.0) e o Azure CLI pela [documentação oficial](https://learn.microsoft.com/cli/azure/install-azure-cli).

Confira a instalação:

```bash
dotnet --list-sdks                         # deve listar 10.x
dotnet --list-runtimes | grep AspNetCore   # deve listar Microsoft.AspNetCore.App 10.x
az extension show --name azure-devops
az account show
```

No CachyOS/Arch, o `run.sh` instala o ASP.NET Core Runtime 10 com `sudo pacman -S --needed aspnet-runtime-10.0` se estiver ausente no `dotnet` padrão. O terminal pode solicitar sua senha e a confirmação da instalação. O script verifica novamente o runtime antes de continuar o build.

Se o ASP.NET Core 10 estiver instalado fora do `dotnet` padrão, informe o executável em `SPRINTFLOW_DOTNET_RUNTIME` (nesse caso, não há instalação automática):

```bash
SPRINTFLOW_DOTNET_RUNTIME=/caminho/para/dotnet ./run.sh
```

## Configurar

Copie `src/SprintFlow.Api/appsettings.Local.example.json` para `src/SprintFlow.Api/appsettings.Local.json` e ajuste organização, projeto e time. `Organization` aceita o nome (`minha-organizacao`) ou a URL (`https://dev.azure.com/minha-organizacao`). O nome do time (`Team`) também aparece no topo da tela. O arquivo local não é versionado. Reinicie o aplicativo depois de mudar esses valores.

## Executar

```bash
./run.sh
```

Abra http://127.0.0.1:5002. Use `./stop.sh` para encerrar. O `run.sh` instala as dependências do frontend na primeira execução (`npm ci`), gera o build do frontend, compila o backend e o inicia em segundo plano. O log fica em `.run/backend.log`.

## Uso

- **Timeline por US:** cada linha é uma US, na ordem do backlog (`StackRank`), com o estado da própria US ao lado do ID. As tasks e os bugs filhos aparecem como barras, posicionadas pelo horário real dentro do dia (cada coluna representa 24h). A área de datas mostra uma semana (cinco dias úteis), com rolagem horizontal para o restante da sprint e os títulos das USs fixos. Ao abrir ou trocar de sprint, o momento atual fica centralizado quando está dentro da sprint atual, respeitando os limites da rolagem; sprints futuras abrem no início. Em telas estreitas, cada dia mantém a largura mínima de 110px.
- **Barras:** primeira linha com `#id` e título da task, segunda com o responsável. A cor indica o estado: concluída, em andamento, em revisão, parada (hachurado) ou prevista. Faixa vermelha indica bloqueio previsto. Clique na barra para ver o histórico com datas e horas.
- **Folgas:** os days off do time inteiro destacam a coluna do dia em vermelho discreto, inclusive nas USs recolhidas. Os individuais aparecem como uma hachura vermelha apenas no trecho da barra da task que coincide com a folga do responsável; passe o mouse na task para ver as datas. Ambos são considerados na previsão. Os dados vêm do último sync; sincronize para carregar as folgas coletivas em caches anteriores.
- **USs concluídas:** vale o estado `Done` da própria US no Azure, independentemente das tasks. São recolhidas por padrão. O `⚙` no topo liga ou desliga esse comportamento, e um duplo clique no título da US expande ou recolhe suas tasks. Um clique abre o modal com título completo, estado, descrição e tasks vinculadas; clicar em uma task abre seus detalhes. O duplo clique em **USER STORY** alterna entre recolher todas as USs e expandir as permitidas pela configuração: TODO, IN PROGRESS e TESTING/TESTINGS quando marcada; todas quando desmarcada. Descrições ausentes no cache aparecem após sincronizar a sprint.
- **Cache local:** os dados ficam salvos em `~/.local/share/SprintFlow/<time>/`. Ao abrir, a tela mostra o último dado salvo e sincroniza com o Azure em segundo plano. O horário do último sync aparece ao lado de **SYNC SPRINT**. Os campos dos work items são consultados em lote via Azure CLI; o histórico completo da sprint atual é lido em paralelo.

Uma pausa só aparece quando houve mudança de estado no Azure DevOps; períodos sem atualização não permitem identificar paradas reais. Não há banco de dados nem escrita no Azure DevOps.

## Desenvolvimento

```bash
dotnet test tests/SprintFlow.Tests
cd src/SprintFlow.Web && npm install && npm run dev
```

O Vite usa o proxy da API em http://127.0.0.1:5002, então o backend precisa estar rodando (`./run.sh`). O build do frontend (`src/SprintFlow.Web/dist`) é copiado para o `wwwroot` do backend na compilação.
