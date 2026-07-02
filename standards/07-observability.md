# DIRETRIZES DE OBSERVABILIDADE, TELEMETRIA E HEALTH CHECKS

**REGRA ABSOLUTA:** Toda a instrumentação da aplicação DEVE ser feita utilizando o padrão OpenTelemetry. É estritamente proibido o uso de SDKs proprietários (vendor-specific) diretamente no código de negócio para geração de logs, métricas ou traces.

---

## 1. Tracing Distribuído e Propagação de Contexto

- **Padrão W3C:** Utilize o padrão W3C Trace Context para propagação de cabeçalhos (`traceparent`, `tracestate`) entre o Frontend, o API Gateway, os BFFs e os Microserviços.
- **Instrumentação em C# (.NET 8):** Não crie classes customizadas para tracing. Utilize EXCLUSIVAMENTE a classe nativa `System.Diagnostics.Activity` e `ActivitySource` para criar spans manuais em regras de negócio complexas.
- Garanta que todas as chamadas HTTP saintes (`HttpClient`) e integrações com o banco de dados (Dapper) tenham a instrumentação automática do OpenTelemetry habilitada no arquivo de injeção de dependência (`Program.cs`).
- A propagação do `TraceId` entre BFFs, APIs de domínio e o Banco de Dados é obrigatória.

---

## 2. Métricas de Negócio

- Utilize a API nativa `System.Diagnostics.Metrics.Meter` para criar métricas customizadas (Counters, Histograms, Observables).
- Crie medidores semânticos que reflitam o domínio da aplicação (ex: `payment_processed_total`, `auth_failure_count`), garantindo que possam ser lidos facilmente em dashboards operacionais.

---

## 3. Logs Estruturados

- **Formato:** Logs devem ser gerados em formato JSON (ex: via Serilog) e **nunca** devem conter dados pessoais (PII). Veja `00-global-rules.md` para a regra de LGPD.
- **Enriquecimento:** Os logs devem ser enriquecidos automaticamente pelo OpenTelemetry com o `TraceId` e `SpanId` da requisição atual, garantindo correlação exata entre o erro no log e a jornada do usuário no tracing.

---

## 4. Exportação (OTLP)

- **Protocolo único:** Todo dado de telemetria (traces, metrics, logs) deve ser exportado utilizando o protocolo OTLP (OpenTelemetry Protocol).
- Configure o `OtlpExporter` no `Program.cs` para enviar os dados para o collector da infraestrutura. Nenhum exporter proprietário (ex: AWS X-Ray SDK direto) deve ser acoplado ao código de negócio.

---

## 5. Health Checks (Liveness e Readiness)

- **Implementação Nativa:** Utilize exclusivamente o pacote nativo `Microsoft.Extensions.Diagnostics.HealthChecks` do .NET 8.
- **Liveness (`/health/live`):** Retorna `200 OK` apenas se a aplicação subiu e a thread principal não está travada. **Nunca** faça chamadas a dependências externas neste endpoint.
- **Readiness (`/health/ready`):** Valida dependências críticas de forma assíncrona. Inclua obrigatoriamente:
  - Verificação de conexão com o banco de dados (PostgreSQL via Dapper)
  - Verificação de conectividade com o Redis (ElastiCache)
  - Verificação de acesso ao SSM Parameter Store (para confirmar que segredos estão acessíveis)
- **Formatação:** A resposta do `Readiness` deve retornar um payload JSON detalhado (via customização do `ResponseWriter` com `HealthReport`), listando o status individual de cada dependência para consumo da infraestrutura e do ECS health check.
