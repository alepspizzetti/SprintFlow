# Regras de domínio

Aplicação: Azure, previsão, métricas e timeline.

- USs: `User Story` e `Product Backlog Item`. Filhos: `Task` e `Bug`, vinculados por `System.Parent`; filhos sem pai são ignorados.
- A ordem segue `Microsoft.VSTS.Common.StackRank`, com fallback para `BacklogPriority`. USs por ordem; tasks dentro da US por ordem e depois ID.
- O estado da própria US determina sua conclusão. Apenas `Done` após a normalização existente conta; `Testing` e `Awaiting Release` ficam pendentes, independentemente das tasks.
- Estados conhecidos das tasks são normalizados para `Todo`, `In Progress`, `Review` e `Done`; estados não reconhecidos são preservados pelo código.
- Histórico de atividade só é carregado para a sprint atual. Tempo sem atualização no Azure não prova uma pausa real.
- A previsão usa esforço restante, capacidade, dias úteis e folgas. Frontend aguarda Backend da mesma US; QA aguarda Backend e Frontend.
- Regras detalhadas e limites estão em `.docs/ARQUITETURA.md`. Consulte a implementação ao mudar classificações, datas, ordenação ou dependências.
- Diferencie o estado real do Azure e a previsão local. A previsão não grava dados no Azure.
