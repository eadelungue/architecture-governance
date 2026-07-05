# DIRETRIZES DE CACHING DISTRIBUÍDO (REDIS)

> **Contexto arquitetural:** O cache de saldo do Ledger Core é invalidado de forma síncrona (após commit da transação). NÃO usar invalidação assíncrona via eventos para dados do Ledger. Consulte `18-ledger-core-architecture.md`.

## 1. Estratégia de Cache-Aside
- O sistema DEVE seguir o padrão **Cache-Aside**. A aplicação verifica se o dado existe no Redis antes de consultar o banco (Dapper).
- Em caso de *Cache Miss*, a aplicação busca no banco, popula o cache e retorna ao usuário.

## 2. Invalidação e Consistência

### 2.1 Cache do Ledger Core (saldo e dados contábeis)
- **Invalidação Síncrona Obrigatória:** Após cada transação de escrita no Ledger (commit no PostgreSQL), o cache de saldo da(s) conta(s) afetada(s) é invalidado **dentro do mesmo request**, de forma síncrona.
- É PROIBIDO depender de eventos assíncronos para invalidar cache de saldo no Ledger — isso causaria leituras stale.
- **TTL curto de segurança:** Itens de saldo no cache DEVEM ter TTL de 5-10 segundos como fallback caso a invalidação síncrona falhe por erro transiente.

### 2.2 Cache de Serviços de Produto (dados de negócio)
- **Event-Driven Invalidation:** Para dados de produto que não são saldo contábil (ex: catálogo de ofertas, elegibilidade), o cache pode ser invalidado via consumo de eventos via **Amazon SQS**.
- **TTL de Segurança:** Todo item no cache de produto DEVE ter um TTL máximo (ex: 30 minutos) para evitar dados "zumbis" caso o evento de invalidação falhe.

## 3. Limitações de Free Tier
- Utilize exclusivamente nós `cache.t3.micro`.
- **Evite Cache Serverless:** O modelo ElastiCache Serverless cobra por GB-hora e requisições, podendo extrapolar o Free Tier.