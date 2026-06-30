# DIRETRIZES DE OBSERVABILIDADE E TELEMETRIA (OPENTELEMETRY)
**REGRA ABSOLUTA:** Toda a instrumentação da aplicação DEVE ser feita utilizando o padrão OpenTelemetry. É estritamente proibido o uso de SDKs proprietários (vendor-specific) diretamente no código de negócio para geração de logs, métricas ou traces.

## 1. Tracing Distribuído e Propagação de Contexto
- **Padrão W3C:** Utilize o padrão W3C Trace Context para propagação de cabeçalhos (`traceparent`, `tracestate`) entre o Frontend, o API Gateway, os BFFs e os Microserviços.
- **Instrumentação em C# (.NET 8):** Não crie classes customizadas para tracing. Utilize EXCLUSIVAMENTE a classe nativa `System.Diagnostics.Activity` e `ActivitySource` para criar spans manuais em regras de negócio complexas.
- O Kiro deve garantir que todas as chamadas HTTP saintes (`HttpClient`) e integrações com o banco de dados (Dapper) tenham a instrumentação automática do OpenTelemetry habilitada no arquivo de injeção de dependência (`Program.cs`).

## 2. Métricas de Negócio (Metrics)
- Utilize a API nativa `System.Diagnostics.Metrics.Meter` para criar métricas customizadas (Counters, Histograms, Observables).
- Crie medidores semânticos que reflitam o domínio da aplicação (ex: `payment_processed_total`, `auth_failure_count`), garantindo que possam ser lidos facilmente em dashboards operacionais.

## 3. Logs Estruturados e Exportação (OTLP)
- **Exportação:** Todo dado de telemetria deve ser exportado utilizando o protocolo OTLP (OpenTelemetry Protocol). Configure o `OtlpExporter` para enviar os dados para o collector da infraestrutura.
- **Enriquecimento de Logs:** Os logs devem ser estruturados (JSON) e enriquecidos automaticamente pelo OpenTelemetry com o `TraceId` e `SpanId` da requisição atual, garantindo a correlação exata entre o erro no log e a jornada do usuário no tracing.

# DIRETRIZES DE OBSERVABILIDADE E HEALTH CHECKS

## 1. Health Checks (Liveness e Readiness)
- **Implementação Nativa:** Utilize exclusivamente o pacote nativo `Microsoft.Extensions.Diagnostics.HealthChecks` do .NET 8.
- **Liveness (`/health/live`):** Retorna `200 OK` apenas se a aplicação subiu e a thread principal não está travada. NUNCA faça chamadas a dependências externas neste endpoint.
- **Readiness (`/health/ready`):** Valida dependências críticas de forma assíncrona. Inclua obrigatoriamente a verificação de conexão com o banco de dados (SQL Server) e serviços vitais da AWS.
- **Formatação:** A resposta do `Readiness` deve retornar um payload JSON detalhado (via customização do `ResponseWriter` com `HealthReport`), listando o status individual de cada dependência para consumo da infraestrutura.

## 2. Telemetria com OpenTelemetry (OTLP)
- **Instrumentação Padrão:** Toda a instrumentação da aplicação DEVE ser feita utilizando bibliotecas do OpenTelemetry. É proibido o uso de SDKs proprietários acoplados no código de negócio.
- **Tracing:** Utilize o padrão W3C Trace Context. A propagação do `TraceId` entre BFFs, APIs de domínio e o Banco de Dados é obrigatória. Crie Spans manuais (`Activity`) apenas para processos de negócio complexos.
- **Logs Estruturados:** Logs devem ser gerados em formato JSON (ex: via Serilog), enriquecidos automaticamente com o `TraceId` e `SpanId`. Nunca logue dados pessoais (PII).