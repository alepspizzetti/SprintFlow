# Diretrizes do Repositório

## Estrutura do Projeto e Organização dos Módulos

O SprintFlow faz previsões sobre o trabalho das sprints do Azure DevOps e exibe uma linha do tempo.

- `sprintflow/`: backend em Python; `server.py` serve HTTP e arquivos estáticos, `forecast.py` agenda o trabalho, `timeline.py` monta as visualizações e `azure_cli.py` cuida das leituras do Azure. Modelos, configuração e cache em memória ficam junto desses módulos.
- `web/`: frontend estático (`index.html`, `app.js`, `styles.css`).
- `tests/`: testes de previsão, linha do tempo e parsers do Azure.
- `.github/`: CI, responsáveis pelo código e regras de branches.
- `appsettings.json`: valores padrão compartilhados; `appsettings.Local.example.json`: modelo de configuração local.
- `.run/`: IDs de processos e logs gerados.

## Comandos de Build, Teste e Desenvolvimento

Use Python 3.11 ou superior; não é necessário instalar pacotes nem executar um build do frontend. O acesso ao Azure exige o Azure CLI, sua extensão `azure-devops` e uma sessão autenticada com `az login`. Copie o exemplo de configuração local para `appsettings.Local.json` e configure a organização, o projeto e a equipe.

- `./run.sh`: inicia o aplicativo em `http://127.0.0.1:5002`; os logs ficam em `.run/backend.log`.
- `./stop.sh`: encerra o servidor em segundo plano.
- `python3 -m sprintflow`: executa em primeiro plano.
- `python3 -m unittest discover -s tests`: executa os testes do backend.
- `node --check web/app.js`: verifica a sintaxe JavaScript; o CI usa Node 22.

## Estilo de Código e Convenções de Nomenclatura

Siga os estilos existentes: indentação de quatro espaços em Python, funções e variáveis em snake_case, classes em PascalCase e modelos tipados. O JavaScript usa dois espaços, funções em camelCase, aspas simples e omite ponto e vírgula; preserve os tipos JSDoc existentes. Não há um formatador ou linter dedicado configurado.

Mantenha as alterações pontuais. Use português nos textos da interface e nas mensagens de erro. Siga a regra de `CONTRIBUTING.md` que proíbe adicionar comentários explicativos no código; documente o raciocínio separadamente.

## Diretrizes de Testes

Use o `unittest` da biblioteca padrão, com arquivos `tests/test_*.py` e métodos `test_*` com nomes descritivos. Adicione casos de regressão ao alterar previsões, parsers ou a conversão de itens de trabalho. Use dados fictícios. Não há um limite numérico de cobertura configurado.

Para alterações na interface, inspecione a sprint atual localmente com as histórias concluídas recolhidas e expandidas por meio do controle de configurações.

## Diretrizes de Commits e Pull Requests

O histórico usa `feat:`, `fix:` e variantes com inicial maiúscula; prefira Conventional Commits em letras minúsculas. Crie branches a partir de `master` usando `feat/`, `fix/`, `docs/`, `refactor/` ou `chore/`.

Envie PRs para `master` descrevendo o problema, a solução e a validação; inclua capturas de tela para alterações na interface. As verificações de API e Web devem passar. Siga as regras de aprovação dos responsáveis pelo código e atualize a documentação afetada.

## Regras de Segurança e Domínio

O acesso ao Azure DevOps é somente leitura: faça as leituras por meio de `AzureDevOpsCliClient`. Preserve a ordenação do backlog por `StackRank`. Nunca inclua em commits configurações locais, arquivos de execução, credenciais ou dados reais de itens de trabalho ou pessoas. A autenticação usa a sessão do Azure CLI; não armazene tokens.
