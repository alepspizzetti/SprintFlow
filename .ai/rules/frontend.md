# Frontend

Aplicação: `web/`.

- HTML, CSS e JavaScript sem framework, build ou npm; preserve o fluxo atual em `web/app.js` (`// @ts-check` com tipos em JSDoc), `web/index.html` e `web/styles.css`.
- Textos visíveis e mensagens ficam em português.
- Timeline por US: cada coluna corresponde a 24 horas; posicionamento considera hora e minuto.
- Empacote barras na ordem do backlog; primeira linha com `#id título`, segunda com responsável.
- Contador e recolhimento de USs usam `isDone` da API, baseado no estado da própria US.
- Preserve `localStorage['sprintflow.collapseDone']` e a expansão manual das USs.
- Ao abrir uma sprint, mostre o cache e sincronize em segundo plano se o dado for anterior à abertura da página.
- Para mudanças visuais, confira USs recolhidas e expandidas, estados de carregamento/erro e os controles afetados. `node --check` sozinho não valida interação.
- O backend serve `web/` diretamente; após alterar o frontend, recarregue a página. Não há build.
- Mudanças de design ou comportamento fora do pedido precisam ser combinadas com o usuário.
