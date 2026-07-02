# DIRETRIZES DE CACHING DISTRIBUÍDO (REDIS)

## 1. Estratégia de Cache-Aside
- O sistema DEVE seguir o padrão **Cache-Aside**. A aplicação verifica se o dado existe no Redis antes de consultar o banco legado (Dapper).
- Em caso de *Cache Miss*, a aplicação busca no banco, popula o cache e retorna ao usuário.

## 2. Invalidação e Consistência
- **Event-Driven Invalidation:** Para transações financeiras, o cache deve ser invalidado ou atualizado através do consumo de eventos via **Amazon SQS** (padrão de mensageria do projeto). Nunca confie apenas no TTL (Time-To-Live).
- **TTL de Segurança:** Todo item no cache DEVE ter um TTL máximo (ex: 30 minutos) para evitar dados "zumbis" caso o evento de invalidação falhe.

## 3. Limitações de Free Tier
- Utilize exclusivamente nós `cache.t3.micro`.
- **Evite Cache Serverless:** O modelo ElastiCache Serverless cobra por GB-hora e requisições, podendo extrapolar o Free Tier.