# SprintFlow

Gantt local para a daily de uma squad. Mostra as USs e tasks da sprint na ordem do backlog do Azure DevOps, com o histórico de estados de cada task e a previsão de término calculada a partir de capacidade, esforço restante e dependências. O Azure DevOps é a fonte da verdade: o aplicativo só lê dados.

## Estrutura

```text
src/
  SprintFlow.Api/      backend ASP.NET Core: API, cache, leitura do Azure e previsão
  SprintFlow.Web/      frontend React + Vite, servido pelo backend
tests/
  SprintFlow.Tests/    testes xUnit
.docs/
  ARQUITETURA.md       arquitetura, fluxos, regras e limites
.github/
  workflows/ci.yml     CI: build e testes da API e build do Web
  rulesets/master.json proteção da branch master
  CODEOWNERS           aprovador obrigatório dos PRs
run.sh / stop.sh       sobe e encerra o aplicativo
```

Detalhes de arquitetura em [`.docs/ARQUITETURA.md`](.docs/ARQUITETURA.md). Para contribuir, veja [`CONTRIBUTING.md`](CONTRIBUTING.md).

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

Se o ASP.NET Core 10 estiver instalado fora do `dotnet` padrão, informe o executável em `SPRINTFLOW_DOTNET_RUNTIME`:

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

- **Timeline por US:** cada linha é uma US, na ordem do backlog (`StackRank`). As tasks e os bugs filhos aparecem como barras, posicionadas pelo horário real dentro do dia (cada coluna representa 24h).
- **Barras:** primeira linha com `#id` e título da task, segunda com o responsável. A cor indica o estado: concluída, em andamento, em revisão, parada (hachurado) ou prevista. Faixa vermelha indica bloqueio previsto. Clique na barra para ver o histórico com datas e horas.
- **USs concluídas:** recolhidas por padrão. O `⚙` no topo liga ou desliga esse comportamento, e clicar no título da US expande ou recolhe.
- **Cache local:** os dados ficam salvos em `~/.local/share/SprintFlow/<time>/`. Ao abrir, a tela mostra o último dado salvo e sincroniza com o Azure em segundo plano. O horário do último sync aparece ao lado de **SYNC SPRINT**.

Uma pausa só aparece quando houve mudança de estado no Azure DevOps; períodos sem atualização não permitem identificar paradas reais. Não há banco de dados nem escrita no Azure DevOps.

## Desenvolvimento

```bash
dotnet test tests/SprintFlow.Tests
cd src/SprintFlow.Web && npm install && npm run dev
```

O Vite usa o proxy da API em http://127.0.0.1:5002, então o backend precisa estar rodando (`./run.sh`). O build do frontend (`src/SprintFlow.Web/dist`) é copiado para o `wwwroot` do backend na compilação.
