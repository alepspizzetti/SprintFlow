# Validação proporcional à mudança

Aplicação: alterações de comportamento e execução de checks.

- Para uma correção, reproduza o problema por um teste ou uma verificação observável antes de corrigir, quando viável.
- Cubra comportamento e risco real; evite testes que apenas espelham a implementação.
- Backend, parser, domínio e contratos: execute `dotnet test tests/SprintFlow.Tests`.
- Frontend: execute `npm --prefix src/SprintFlow.Web run build`; para interação, valide no navegador quando disponível.
- Renomeações que atravessam camadas: confira referências, testes e build dos consumidores.
- Scripts: valide sintaxe e cenários representativos sem instalar pacotes nem atingir serviços por meio de mocks quando isso bastar.
- Documentação e configuração de IA: execute `python3 .ai/scripts/validate.py`; não reinicie a aplicação só por editar documentos.
- Se ocorrer `NETSDK1226`, use `-p:AllowMissingPrunePackageData=true` no comando .NET, como já faz o `run.sh`.
- Depois dos checks adequados aprovarem, repita somente quando uma nova mudança ou dúvida justificar.
- Registre verificações executadas e limitações. Não confunda sintaxe válida com carregamento real pelo harness, nem build com validação visual.
