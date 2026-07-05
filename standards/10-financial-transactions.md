# DIRETRIZES PARA TRANSAÇÕES FINANCEIRAS E CRÍTICAS
**IMPORTANTE:** O sistema lida com alta volumetria, saldos e pontuações críticas. A consistência dos dados é inegociável.

> **Contexto arquitetural:** O motor contábil (Ledger Core) opera 100% síncrono e não publica eventos. Consulte `18-ledger-core-architecture.md` para a separação completa Core vs. Produto.

## 1. Precisão Numérica e Tipagem (C#)
- **REGRA ABSOLUTA:** É TERMINANTEMENTE PROIBIDO o uso de `float` ou `double` para representar valores monetários, saldos ou pontuações.
- Utilize EXCLUSIVAMENTE o tipo `decimal` no C# para garantir a precisão matemática e evitar erros de arredondamento.

## 2. Idempotência
- Qualquer operação de escrita que altere saldo, efetue pagamentos ou processe transações DEVE ser idempotente.
- O serviço de produto chamador DEVE gerar e enviar o cabeçalho `Idempotency-Key` ao Ledger Core.
- O Ledger implementa uma checagem rápida (via Redis ou tabela dedicada no banco via Dapper) para verificar se a `Idempotency-Key` já foi processada. Se sim, retorna o mesmo resultado da operação anterior sem reprocessar (HTTP 200).
- Para endpoints públicos de produto expostos no API Gateway, o produto também DEVE validar a `Idempotency-Key` recebida do cliente externo antes de orquestrar chamadas ao Ledger.

## 3. Consistência — Separação de Responsabilidades
- **Ledger Core (motor contábil):** Opera exclusivamente de forma síncrona. NÃO implementa Outbox. NÃO publica eventos. Toda operação é uma transação ACID com resposta imediata.
- **Serviços de Produto (regras de negócio):** Responsáveis por propagar eventos após confirmar a transação no Ledger.

### 3.1 Padrão Outbox — Aplicável no Ledger e nos Serviços de Produto

#### No Ledger Core (eventos de notificação)
- O Ledger persiste um evento de fato contábil na tabela Outbox do SEU banco, dentro da mesma transação ACID que altera saldo.
- Estes eventos são **informativos** (notificação de que algo aconteceu) e publicados no SNS topic `ledger-events` por um worker dedicado.
- O Ledger NUNCA consome eventos de volta — ele apenas emite.

#### Nos Serviços de Produto (eventos de domínio)
- **Sem Dual-Writes:** Nunca tente salvar dados no banco relacional e publicar um evento em fila (ex: SNS/SQS) no mesmo fluxo sem garantia de entrega.
- **Padrão Outbox nos Produtos:** Após receber confirmação síncrona do Ledger, o serviço de produto persiste o evento de domínio na tabela Outbox do SEU banco (separado do Ledger), dentro de uma transação local.
- O código de negócio gerado deve apenas persistir a intenção na tabela; a publicação real na mensageria será feita por um *worker* separado.

### 3.2 Fluxo Correto de uma Transação Financeira
1. Cliente externo chama API de Produto (via API Gateway)
2. Produto valida regras de negócio e elegibilidade
3. Produto chama Ledger Core de forma síncrona (HTTP interno via Cloud Map)
4. Ledger executa transação ACID (débito + crédito) e retorna confirmação
5. Produto persiste evento Outbox no SEU banco local
6. Worker do produto publica evento na mensageria (SNS/SQS)