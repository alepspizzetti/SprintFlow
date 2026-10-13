# Code-Style

- Funções e métodos próprios, incluindo auxiliares e testes, devem ter nomes em português do Brasil, sem acentos nos identificadores. 
- Python: `snake_case` em funções e variáveis, `PascalCase` em classes. JavaScript: `camelCase`.
- Use somente a biblioteca padrão do Python e JavaScript sem dependências; nova dependência precisa ser combinada.
- Preserve o estilo do arquivo e as convenções existentes; não formate trechos fora do pedido.
- Prefira corrigir a origem do dado ou da decisão. Evite compensações em outra camada.
- Use a solução mais simples que atenda ao caso real; não introduza abstrações, opções ou dependências para necessidades hipotéticas.
- Não adicione comentários no código. Registre explicações de arquitetura na documentação.
- Remova imports e símbolos que a sua mudança deixou sem uso; preserve código preexistente fora do escopo.
- Não misture refactors, atualizações de dependências ou limpeza com uma correção.
- Interfaces, mensagens de erro e documentação do projeto ficam em português; identificadores seguem o estilo da linguagem e do código existente.
- Declare incertezas relevantes antes da implementação. Investigue primeiro o que puder ser verificado no repositório.
- Cada linha alterada deve ter relação com o objetivo. Revise o diff antes de entregar.
