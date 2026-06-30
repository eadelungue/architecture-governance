# DIRETRIZES PARA TRANSAÇÕES FINANCEIRAS E CRÍTICAS
**IMPORTANTE:** O sistema lida com alta volumetria, saldos e pontuações críticas. A consistência dos dados é inegociável.

## 1. Precisão Numérica e Tipagem (C#)
- **REGRA ABSOLUTA:** É TERMINANTEMENTE PROIBIDO o uso de `float` ou `double` para representar valores monetários, saldos ou pontuações.
- Utilize EXCLUSIVAMENTE o tipo `decimal` no C# para garantir a precisão matemática e evitar erros de arredondamento.

## 2. Idempotência
- Qualquer operação de escrita que altere saldo, efetue pagamentos ou processe transações DEVE ser idempotente.
- A IA deve exigir e validar um cabeçalho `Idempotency-Key` (via API Gateway ou caller) antes de processar a requisição.
- Implemente uma checagem rápida (ex: via Redis ou tabela dedicada no banco via Dapper) para verificar se a `Idempotency-Key` já foi processada. Se sim, retorne o mesmo resultado da operação anterior sem reprocessar (HTTP 200).

## 3. Consistência e Padrão Outbox
- **Sem Dual-Writes:** Nunca tente salvar dados no banco relacional e publicar um evento em fila (ex: Service Bus/SQS) no mesmo fluxo sem garantia de entrega.
- **Padrão Outbox Obrigatório:** Para propagar eventos após uma transação financeira, utilize o padrão *Transactional Outbox*. Salve o evento na mesma transação de banco de dados (Dapper) que altera o estado da entidade, utilizando uma tabela de outbox.
- O código de negócio gerado deve apenas persistir a intenção na tabela; a publicação real na mensageria será feita por um *worker* separado.