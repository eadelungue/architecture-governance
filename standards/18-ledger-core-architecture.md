# DIRETRIZES DE ARQUITETURA DO LEDGER CORE

**IMPORTANTE:** O Ledger é o motor contábil central do sistema CorePoints. Ele opera exclusivamente como serviço interno síncrono, sem exposição pública e sem lógica de negócio de produto.

---

## 1. Princípio Fundamental: Separação Core vs. Produto

O sistema segue uma separação rígida em duas camadas:

| Camada | Responsabilidade | Modelo de Comunicação |
|--------|-----------------|----------------------|
| **Ledger Core** | Partidas dobradas, débito/crédito, saldo, extrato | 100% síncrono |
| **Serviços de Produto** | Regras de negócio (cashback, loyalty, transferências) | Assíncrono entre si, síncrono com o Ledger |

**REGRA ABSOLUTA:** O Ledger Core NÃO contém regras de negócio de produto. Ele é um motor contábil genérico que executa operações de débito e crédito sob demanda dos serviços de produto.

---

## 2. Ledger Core — Regras de Operação

### 2.1 Comunicação Exclusivamente Síncrona

- O Ledger Core opera **100% síncrono** (request → transação ACID → response).
- É PROIBIDO o Ledger consumir filas, processar eventos assíncronos ou ter workers de background que alterem saldo.
- Toda alteração de saldo ocorre dentro de uma transação PostgreSQL atômica com resposta imediata ao chamador.
- O saldo retornado em qualquer resposta é SEMPRE o saldo real e atualizado (consistência forte).

### 2.2 API Interna (Não Exposta Publicamente)

- O Ledger Core NÃO é acessível via API Gateway público.
- Apenas serviços de produto dentro da rede privada (VPC) podem chamar o Ledger.
- A comunicação interna usa **Service Discovery** (AWS Cloud Map) via HTTP síncrono.
- O API Gateway público expõe APENAS endpoints dos serviços de produto.

### 2.3 Operações Suportadas pelo Ledger

O Ledger Core expõe apenas operações contábeis primitivas:

| Operação | Descrição | Retorno |
|----------|-----------|---------|
| `POST /accounts` | Criar titular e conta(s) | Account ID |
| `POST /transactions` | Registrar transação (débito + crédito) | Transaction ID + saldos atualizados |
| `GET /accounts/{id}/balance` | Consultar saldo atual | Saldo (decimal) |
| `GET /accounts/{id}/statement` | Extrato paginado | Lista de movimentações |
| `GET /transactions/{id}` | Status de uma transação | Detalhes da transação |

### 2.4 Idempotência no Ledger

- Toda operação de escrita (`POST /transactions`) EXIGE o header `Idempotency-Key`.
- O Ledger verifica via Redis (ou tabela dedicada) se a chave já foi processada.
- Se já processada: retorna o resultado anterior (HTTP 200) sem reprocessar.
- A `Idempotency-Key` é gerada pelo serviço de produto chamador.

### 2.5 Eventos de Notificação (Read-Only, Pós-Commit)

- O Ledger Core EMITE eventos de fato contábil após o commit da transação.
- Estes eventos são **notificações read-only** — informam que algo aconteceu, mas NÃO alteram estado.
- O Ledger NUNCA consome eventos para alterar saldo ou executar transações.
- O mecanismo de publicação usa **Transactional Outbox** no banco do Ledger para garantir entrega.

**Fluxo:**
1. Produto chama Ledger (síncrono)
2. Ledger executa transação ACID + persiste evento Outbox na MESMA transação → commit
3. Ledger retorna resposta ao produto chamador (síncrono)
4. Worker do Ledger publica evento no SNS topic `ledger-events` (assíncrono, pós-commit)

**Consumidores dos eventos do Ledger:**
- **Data Lake** — para analytics, auditoria e relatórios (S3/Kinesis Firehose)
- **Serviços de Produto** — podem assinar para reagir a movimentações de qualquer origem

**REGRA ABSOLUTA:** Os eventos do Ledger são INFORMATIVOS. Nenhum consumidor pode usar um evento do Ledger para solicitar uma nova operação de volta ao Ledger sem passar pela camada de produto (evita loops).

### 2.6 Formato dos Eventos do Ledger

Os eventos seguem o padrão Event Notification (apenas IDs e tipo, sem payload completo):

```json
{
  "eventType": "TransactionRecorded",
  "transactionId": "uuid",
  "debitAccountId": "uuid",
  "creditAccountId": "uuid",
  "amount": 100.00,
  "timestamp": "2025-01-01T00:00:00Z",
  "correlationId": "X-Correlation-ID original"
}
```

---

## 3. Serviços de Produto — Regras de Operação

### 3.1 Responsabilidades

Os serviços de produto contêm:
- Regras de negócio específicas (ex: regras de cashback, limites de transferência, expiração de pontos)
- Validações de negócio (ex: elegibilidade, limites diários)
- Orquestração de fluxos que envolvem múltiplas chamadas ao Ledger
- Publicação de eventos de domínio para outros produtos

### 3.2 Comunicação com o Ledger

- Toda chamada do produto ao Ledger é **síncrona** (HTTP via Cloud Map).
- O produto DEVE aplicar políticas de resiliência (Polly): retry com jitter + circuit breaker.
- O produto DEVE repassar o `X-Correlation-ID` em todas as chamadas ao Ledger.
- O produto gera a `Idempotency-Key` e a envia ao Ledger.

### 3.3 Comunicação entre Produtos

- Produtos se comunicam entre si de forma **assíncrona** via SNS/SQS.
- O padrão Transactional Outbox é usado **nos serviços de produto** (não no Ledger).
- Cada produto é responsável por sua própria consistência eventual.

### 3.4 Exposição Pública

- Apenas os serviços de produto expõem endpoints no API Gateway público.
- Cada produto define seus próprios DTOs de entrada/saída (não expõe DTOs do Ledger).
- Rate limiting e throttling são configurados por produto no API Gateway.

---

## 4. Modelo de Dados — Separação de Bancos

### 4.1 Banco do Ledger (Dedicado)

- O Ledger Core possui seu **próprio banco de dados PostgreSQL** (ou schema isolado).
- Nenhum outro serviço acessa diretamente as tabelas do Ledger.
- As tabelas do Ledger são otimizadas para operações contábeis de alta performance.
- Connection pooling (PgBouncer ou RDS Proxy) é obrigatório em produção.

### 4.2 Banco dos Produtos (Separado)

- Cada serviço de produto pode ter seu próprio banco/schema.
- Os produtos armazenam estado de negócio (regras, configurações, histórico de produto).
- A tabela Outbox fica no banco do produto, NÃO no banco do Ledger.

### 4.3 Princípio de Isolamento

- É PROIBIDO qualquer serviço de produto fazer queries diretamente nas tabelas do Ledger.
- Toda consulta de saldo/extrato passa pela API síncrona do Ledger.
- Isso garante que o Ledger pode evoluir seu schema interno sem quebrar produtos.

---

## 5. Escalabilidade

### 5.1 Estratégia de Escala do Ledger

| Dimensão | Estratégia |
|----------|-----------|
| Compute (ECS Fargate) | Auto-scaling horizontal por CPU/request count |
| Database (PostgreSQL) | RDS com read replicas para consultas + instância principal para writes |
| Cache (Redis) | Cache-aside para saldos com invalidação síncrona pós-transação |
| Connection Pool | PgBouncer ou RDS Proxy obrigatório |

### 5.2 Throughput Esperado

| Ambiente | Instância RDS | TPS Estimado (writes) | Tasks Fargate |
|----------|--------------|----------------------|---------------|
| DEV | db.t4g.micro | 100-200 TPS | 1 |
| PROD | db.r6g.large + RDS Proxy | 2.000-5.000 TPS | 4-8 (auto-scale) |

### 5.3 Serialização por Conta

- Transações na mesma conta são serializadas via `SELECT ... FOR UPDATE` no PostgreSQL.
- Transações em contas diferentes são totalmente paralelas (sem contenção).
- O throughput máximo por conta individual é limitado pela latência do banco (~500-1000 TPS por conta).
- O throughput global é limitado pelo número de conexões e capacidade do RDS.

---

## 6. Cache de Saldo

- O saldo pode ser cacheado em Redis com TTL curto (5-10 segundos) para consultas de leitura.
- Após qualquer transação de escrita no Ledger, o cache de saldo da conta afetada é **invalidado sincronamente** (dentro do mesmo request, após commit).
- NÃO usar invalidação assíncrona via eventos para o cache de saldo do Ledger (evitar leitura stale).

---

## 7. Resumo de Decisões Arquiteturais

| Decisão | Escolha | Justificativa |
|---------|---------|---------------|
| Modelo do Ledger | Síncrono puro (não consome eventos) | Consistência forte, sem race conditions |
| Eventos do Ledger | Emite notificações pós-commit (Outbox) | Data Lake, auditoria, produtos reativos |
| Exposição do Ledger | API interna (Cloud Map) | Controle de acesso, isolamento |
| Banco do Ledger | Isolado (schema/instância dedicada) | Performance, evolução independente |
| Comunicação Produto→Ledger | HTTP síncrono | Garantia de resposta imediata |
| Comunicação Produto→Produto | Assíncrono (SNS/SQS + Outbox) | Desacoplamento |
| Cache de saldo | Redis com invalidação síncrona | Evita leitura inconsistente |
| Serialização de escrita | `SELECT ... FOR UPDATE` por conta | Evita race condition intra-conta |
