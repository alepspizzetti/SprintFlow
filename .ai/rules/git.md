# Git, PRs e CI

Aplicação: branches, commits, PRs e pipelines.

- Preserve mudanças preexistentes. Não use reset, limpeza destrutiva ou descarte de arquivos para obter um diff limpo.
- `master` é protegida: somente PR, uma aprovação do code owner e checks `API` e `Web`.
- Configuração: `.github/workflows/ci.yml`, `.github/rulesets/master.json` e `.github/CODEOWNERS`.
- Se renomear jobs, atualize os contextos correspondentes no ruleset.
- Convenções de branches e commits estão em `CONTRIBUTING.md`.
- Só faça commit, push, merge ou publicação quando isso estiver autorizado na tarefa.
- PR: explique o problema, o comportamento resultante, a validação e limites relevantes. Use `.ai/templates/pull-request.md` como modelo opcional.
- Regras que precisam ser garantidas em toda contribuição devem ser verificadas por CI ou proteção do repositório.
