# SprintFlow — índice para agentes

Todas as instruções compartilhadas ficam em `.ai/`. Este arquivo orienta sua leitura; os caminhos partem da raiz do repositório.

## Leitura obrigatória

Antes de qualquer tarefa, leia `.ai/rules/general.md`, `.ai/rules/code-style.md` e `.ai/rules/security.md`. Depois, carregue somente os documentos relacionados ao pedido.

## O que ler por tarefa

| Quando | Arquivos obrigatórios |
| --- | --- |
| Backend, API, Azure, cache ou previsão | `.ai/rules/backend.md`, `.ai/rules/domain.md` |
| Tela, timeline ou estilos | `.ai/rules/frontend.md`, `.ai/rules/domain.md` |
| Mudança de comportamento ou execução de checks | `.ai/rules/testing.md` |
| Pastas, componentes, rotas, fluxos, cache, integrações ou scripts de execução | `.ai/rules/documentation.md` |
| Branches, commits, PRs ou CI | `.ai/rules/git.md` |
| Implementação ou correção | `.ai/workflows/implement.md`; para bugs, também `.ai/workflows/fix-bug.md` |
| Revisão de código | `.ai/workflows/review.md` |
| Lentidão ou otimização | `.ai/workflows/performance.md` |
| Validação, execução ou reinício | `.ai/workflows/validate.md` |
| Organização das instruções de IA | `.ai/README.md` |

## Skills e perfis compartilhados

Leia o `SKILL.md` correspondente quando o pedido se enquadrar na descrição:

| Pedido | Documento |
| --- | --- |
| Verificar alterações ou execução | `.ai/skills/sprintflow-validate/SKILL.md` |
| Revisar diff, branch ou arquivos | `.ai/skills/sprintflow-review/SKILL.md` |
| Atualizar documentação de estrutura, fluxo ou domínio | `.ai/skills/sprintflow-docs/SKILL.md` |

Perfis de revisão e investigação ficam em `.ai/agents/reviewer.md` e `.ai/agents/investigator.md`. Siga o perfil pertinente diretamente; delegue somente quando a tarefa ou uma instrução aplicável pedir delegação.

## Padrão de commits

- Use esse layout para criar o commit
- `Feat, Fix, Perf` são os tipos disponiveis
- O titulo deve resumir o que foi feito
- As descrições das implementações devem ser breves e diretas
```markdown
{tipo}:{breve titulo}

-> {Feature}: {descriação do que foi feito}
-> {Feature}: {descriação do que foi feito}
```

## Outros recursos

- Organização e manutenção: `.ai/README.md`.
- Modelos editáveis: `.ai/templates/`.
- Avaliação das instruções: `.ai/evals/scenarios.md`.
- Orientação para hooks futuros: `.ai/hooks/README.md`.
- Verificação estrutural: `python3 .ai/scripts/validate.py`.

Skills e perfis são lidos por estas instruções; não há registro nativo de skills ou subagentes no projeto.
