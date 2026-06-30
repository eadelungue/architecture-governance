# DIRETRIZES DE RESILIÊNCIA E TOLERÂNCIA A FALHAS

## 1. Políticas de Resiliência (Polly)
- **Nenhuma Chamada Nua:** É OBRIGATÓRIO o uso do framework Polly (ou Polly v8 nativo no .NET 8 via `Microsoft.Extensions.Http.Resilience`) para TODAS as integrações HTTP saintes (`HttpClient`).
- **Retry e Circuit Breaker:** Toda chamada externa deve ter, no mínimo:
  - Uma política de *Retry Exponencial* com *Jitter* (para falhas transientes como HTTP 502, 503 e 504).
  - Um *Circuit Breaker* configurado para abrir e proteger o sistema downstream após falhas consecutivas, falhando rápido (Fail Fast) e retornando HTTP 503 ou um *fallback* seguro.

## 2. Timeouts e Cancelamentos Agressivos
- **CancellationToken:** TODO método assíncrono gerado (Controllers, Use Cases, Dapper Queries, HttpClients) DEVE receber e repassar o `CancellationToken`. Nunca ignore o token de cancelamento.
- **Limites de Tempo:** Defina *timeouts* estritos no `HttpClient`. Nenhuma requisição para serviços de terceiros deve ficar pendente indefinidamente travando as threads da aplicação.

## 3. Bulkhead e Isolamento
- Assegure que as chamadas a serviços lentos (ex: relatórios, integrações legadas) não consumam o mesmo pool de conexões ou limite de concorrência dos fluxos críticos (como autorização de transações). Aplique *Rate Limiting* ou isolamento de portas quando solicitado.