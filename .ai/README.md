# Guia da estrutura compartilhada de IA

## Escolher onde escrever


| Necessidade                                | Local                    | Exemplo                                                                   |
| ------------------------------------------ | ------------------------ | ------------------------------------------------------------------------- |
| Orientação para toda tarefa                | `rules/general.md`       | escopo, preservação de mudanças e critérios de conclusão                  |
| Regra de uma área                          | `rules/`                 | estilo, backend, frontend, domínio, segurança, testes, documentação e Git |
| Procedimento com entrada e saída           | `workflows/`             | implementar, corrigir bug, revisar, validar ou medir desempenho           |
| Capacidade reutilizável                    | `skills/<nome>/SKILL.md` | validação, revisão e manutenção de documentação                           |
| Responsabilidade especializada             | `agents/`                | revisor e investigador                                                    |
| Documento para copiar e adaptar            | `templates/`             | regra, workflow, skill, agente, plano, PR e decisão técnica               |
| Cenário para avaliar o comportamento da IA | `evals/`                 | US Done com task ainda aberta                                             |
| Verificação determinística                 | `scripts/`               | integridade de referências e metadados                                    |
| Automação por evento                       | `hooks/`                 | orientação para futura configuração; nenhum hook ativo                    |


## Skills disponíveis


| Documento                                 | Quando ler                                                         |
| ----------------------------------------- | ------------------------------------------------------------------ |
| `.ai/skills/sprintflow-validate/SKILL.md` | selecionar e executar checks, build ou reinício                    |
| `.ai/skills/sprintflow-review/SKILL.md`   | revisar uma alteração local delimitada                             |
| `.ai/skills/sprintflow-docs/SKILL.md`     | atualizar documentação após mudanças de estrutura ou comportamento |
