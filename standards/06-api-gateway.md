# DIRETRIZES DE API GATEWAY E BORDAS

> **Contexto arquitetural:** O API Gateway público expõe APENAS APIs de serviços de produto. O Ledger Core é acessível somente via rede interna (Cloud Map). Consulte `18-ledger-core-architecture.md`.

## 1. Exposição Pública (API Gateway)
- **Contrato Front-Back:** Todo roteamento de entrada de clientes externos passa obrigatoriamente por um API Gateway.
- **Apenas Produtos:** O API Gateway público roteia EXCLUSIVAMENTE para serviços de produto. O Ledger Core NÃO é exposto no API Gateway.
- **Rate Limiting por Produto:** Cada serviço de produto tem seu próprio throttling configurado no API Gateway, isolando o impacto entre produtos.

## 2. Comunicação Interna (Service Discovery)
- Serviços de produto se comunicam com o Ledger Core via **AWS Cloud Map** (Service Discovery) na rede privada.
- A comunicação interna usa HTTP síncrono sem passar pelo API Gateway.
- Headers de correlação (`X-Correlation-ID`) DEVEM ser repassados nas chamadas internas ao Ledger.

## 3. Headers de Correlação
- Toda requisição de entrada receberá um header de correlação (ex: `X-Correlation-ID`). É **obrigatório** que os microserviços e BFFs em C# capturem este header e o repassem para qualquer chamada subsequente (outras APIs, filas AWS, banco de dados, e chamadas ao Ledger).

## 4. Rate Limiting e Circuit Breaker
- Ao gerar código para chamadas HTTP de saída (ex: `HttpClient` no C#), inclua políticas de resiliência usando Polly (Retries exponenciais e Circuit Breaker). Nenhuma API interna deve falhar em cascata.
- Chamadas de produto ao Ledger DEVEM ter circuit breaker configurado para proteger o Ledger de sobrecarga.

## 5. Terminação SSL/TLS
- Assuma que o tráfego chega descriptografado no container/lambda apenas após passar pelo Gateway, mas a validação do token do Cognito deve ocorrer no nível da aplicação (nos serviços de produto).
- O Ledger Core valida que a chamada veio de um serviço interno autorizado (via security group ou service mesh), NÃO via JWT do Cognito.