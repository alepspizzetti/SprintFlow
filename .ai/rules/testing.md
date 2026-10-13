# Validação proporcional à mudança

Aplicação: alterações de comportamento e execução de checks.

- Para uma correção, reproduza o problema por um teste ou uma verificação observável antes de corrigir, quando viável.
- Cubra comportamento e risco real; evite testes que apenas espelham a implementação.
- Backend, parser, domínio e contratos: execute `python3 -m unittest discover -s tests`.
- Frontend: execute `node --check web/app.js`; para interação, valide no navegador quando disponível.
- Renomeações que atravessam camadas: confira referências, testes e `node --check` dos consumidores.
- Scripts: valide sintaxe e cenários representativos sem instalar pacotes nem atingir serviços por meio de mocks quando isso bastar.
- Documentação e configuração de IA: execute `python3 .ai/scripts/validate.py`; não reinicie a aplicação só por editar documentos.
- Depois dos checks adequados aprovarem, repita somente quando uma nova mudança ou dúvida justificar.
- Registre verificações executadas e limitações. Não confunda sintaxe válida com carregamento real pelo harness, nem `node --check` com validação visual.
