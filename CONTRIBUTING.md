# Como contribuir

Obrigado por ajudar a melhorar o SprintFlow. Este guia mostra como preparar o ambiente, fazer uma mudança e abrir um pull request.

## Antes de começar

1. Leia o [`README.md`](README.md) para instalar as dependências e configurar o `appsettings.Local.json`.
2. Leia a [arquitetura](.docs/ARQUITETURA.md) para entender camadas, fluxos e regras.
3. Para mudanças grandes ou de comportamento, abra uma issue antes e combine a abordagem.

## Fluxo de trabalho

1. Crie uma branch a partir da `master`:
   ```bash
   git checkout -b feat/descricao-curta
   ```
   Prefixos: `feat/`, `fix/`, `docs/`, `refactor/`, `chore/`.
2. Faça a mudança com o menor diff possível. Não misture refactor com correção ou feature.
3. Valide localmente (seção abaixo).
4. Escreva commits curtos no padrão [Conventional Commits](https://www.conventionalcommits.org/pt-br/):
   ```text
   feat: recolher USs concluídas por padrão
   fix: tratar Bug como item filho da US
   ```
5. Abra o pull request para a `master` descrevendo o problema, a solução e como validou. Inclua um screenshot quando a tela mudar.

## Validação local

```bash
dotnet test tests/SprintFlow.Tests
cd src/SprintFlow.Web && npm run build
./stop.sh && ./run.sh
```

Abra http://127.0.0.1:5002 e confira a sprint atual. Para mudanças de tela, teste com as USs concluídas recolhidas e expandidas (`⚙` no topo).

## Regras do projeto

- **Azure DevOps é somente leitura.** Nunca adicione comando ou chamada que escreva no Azure. Toda leitura passa pelo `AzureDevOpsCliClient`, via `az` CLI.
- **Ordem do backlog.** A tela deve respeitar a ordem do Azure (`StackRank`). Não reordene USs nem tasks por outro critério.
- **Sem comentários no código.** Se algo precisar de explicação, registre em `AGENTS.md` ou `.docs/ARQUITETURA.md`.
- **Textos em português**, tanto na interface quanto nas mensagens de erro.
- **Testes** para regras de previsão, parser e conversão de work items, quando a mudança alterar comportamento.
- **Documentação junto com o código.** Mudanças na estrutura (pastas, camadas, rotas, fluxos, cache, integrações, `run.sh`/`stop.sh`) atualizam `.docs/ARQUITETURA.md`, `AGENTS.md` e `README.md` no mesmo pull request.

## Segurança e dados

- Não versione `src/SprintFlow.Api/appsettings.Local.json`, `.run/` nem arquivos de cache.
- Não inclua tokens, PATs, nomes de organização, títulos de work items ou nomes de pessoas em código, testes, screenshots públicos ou issues. Use dados fictícios.
- A autenticação é sempre a sessão do `az login`; o projeto não deve ler nem guardar credenciais.

## Branch `master` protegida

A `master` só recebe mudanças por pull request. As regras ficam no ruleset `.github/rulesets/master.json`:

- push direto, force-push e exclusão da branch são bloqueados;
- o PR precisa de 1 aprovação do code owner (`.github/CODEOWNERS`); aprovações antigas caem a cada novo push;
- os checks **API** (`dotnet build` + `dotnet test`) e **Web** (`npm ci` + `npm run build`) do workflow `.github/workflows/ci.yml` precisam passar, com a branch atualizada em relação à `master`;
- administradores do repositório podem fazer merge do próprio PR sem a aprovação, mas ainda via PR.

Para aplicar ou reaplicar o ruleset (requer permissão de administrador):

```bash
gh api repos/alepspizzetti/SprintFlow/rulesets --method POST --input .github/rulesets/master.json
```

## Checklist do pull request

- [ ] `dotnet test` e `npm run build` passam.
- [ ] A tela foi conferida no navegador.
- [ ] Nenhuma escrita no Azure DevOps foi adicionada.
- [ ] Documentação atualizada, se a estrutura mudou.
- [ ] Sem dados sensíveis no diff.
