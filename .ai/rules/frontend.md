# Frontend

Aplicação: `src/SprintFlow.Web/`.

- React, Vite e TypeScript; preserve o fluxo atual em `src/App.tsx` e `src/styles.css`.
- Textos visíveis e mensagens ficam em português.
- Timeline por US: cada coluna corresponde a 24 horas; posicionamento considera hora e minuto.
- Empacote barras na ordem do backlog; primeira linha com `#id título`, segunda com responsável.
- Contador e recolhimento de USs usam `isDone` da API, baseado no estado da própria US.
- Preserve `localStorage['sprintflow.collapseDone']` e a expansão manual das USs.
- Ao abrir uma sprint, mostre o cache e sincronize em segundo plano se o dado for anterior à abertura da página.
- Para mudanças visuais, confira USs recolhidas e expandidas, estados de carregamento/erro e os controles afetados. Build sozinho não valida interação.
- Depois de alterar o frontend servido pelo backend, faça build e reinício conforme o workflow de validação.
- Mudanças de design ou comportamento fora do pedido precisam ser combinadas com o usuário.
