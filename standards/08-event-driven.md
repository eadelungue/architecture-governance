# DIRETRIZES DE ARQUITETURA ORIENTADA A EVENTOS

> **Contexto arquitetural:** O Ledger Core NÃO publica nem consome eventos. A arquitetura orientada a eventos se aplica APENAS aos serviços de produto e à comunicação entre eles. Consulte `18-ledger-core-architecture.md`.

## 1. Escopo de Aplicação
- Eventos assíncronos (SNS/SQS) são usados **exclusivamente na camada de Produto**.
- O Ledger Core opera 100% síncrono e NÃO participa do barramento de eventos.
- Serviços de produto publicam eventos de domínio (ex: `CashbackCredited`, `TransferCompleted`) APÓS receberem confirmação síncrona do Ledger.

## 2. Idempotência Obrigatória
- Todo consumidor de eventos (Lambdas, SQS listeners) nos serviços de produto DEVE ser desenhado de forma idempotente.
- O código deve verificar se o evento (baseado no `MessageId` ou `IdempotencyKey`) já foi processado antes de executar qualquer operação (incluindo chamadas ao Ledger).
- Se o consumidor precisa chamar o Ledger, ele DEVE enviar a `Idempotency-Key` para garantir que o Ledger não reprocesse.

## 3. Dead Letter Queues (DLQ)
- Todo fluxo de mensageria gerado deve prever arquiteturalmente o roteamento de falhas para uma DLQ após 3 tentativas malsucedidas.

## 4. Tamanho de Payload
- Eventos não devem carregar o estado inteiro da entidade (Event Carried State Transfer), mas sim operar preferencialmente como Event Notification (carregando apenas o ID e o tipo de alteração), forçando o consumidor a buscar os dados atualizados na API de domínio (do produto ou do Ledger via chamada síncrona) se necessário.

## 5. Padrão de Publicação (Outbox nos Produtos)
- Serviços de produto DEVEM usar o padrão Transactional Outbox para publicar eventos.
- A tabela Outbox fica no banco do próprio produto, NÃO no banco do Ledger.
- O fluxo completo é: Produto confirma no Ledger → persiste evento Outbox localmente → worker publica no SNS/SQS.