# DIRETRIZES DE APDEX E MONITORAMENTO DE EXPERIÊNCIA DO USUÁRIO

**IMPORTANTE:** O Apdex (Application Performance Index) é uma métrica OBRIGATÓRIA para todas as APIs expostas no API Gateway. Ele traduz a latência técnica em uma nota de satisfação do usuário (0 a 1), alinhando engenharia com experiência real.

> Referência: Complementa `07-observability.md` (OpenTelemetry, métricas, traces). O Apdex é uma camada de governança SOBRE as métricas já existentes.

---

## 1. Conceito e Fórmula do Apdex

O Apdex categoriza cada requisição em três grupos baseados no Limite T (tempo alvo) de cada API:

| Categoria | Condição | Peso |
|-----------|----------|------|
| **Satisfeita** | Latência ≤ T E status < 500 | 1.0 |
| **Tolerante** | T < Latência ≤ 4T E status < 500 | 0.5 |
| **Frustrada** | Latência > 4T OU status ≥ 500 | 0.0 |

**Fórmula:**
```
Apdex = (Satisfied + Tolerating × 0.5) / Total
```

**REGRA ABSOLUTA:** Erros HTTP 4xx (validação do cliente, autenticação inválida, recurso não encontrado) NÃO penalizam o Apdex. Apenas lentidão e erros HTTP 5xx derrubam o índice.

---

## 2. Faixas de Classificação (Semáforo)

| Faixa | Apdex | Significado | Ação |
|-------|-------|-------------|------|
| 🟢 Excelente | 0.94 – 1.00 | Usuários satisfeitos | Nenhuma |
| 🟡 Aceitável | 0.85 – 0.93 | Performance tolerável | Investigar tendência |
| 🔴 Crítico | < 0.85 | Usuários frustrados | Alerta + ação imediata |

---

## 3. Limite T — Regras de Governança

### 3.1 Proibição de T Único

É PROIBIDO usar o mesmo Limite T para todas as APIs. Cada endpoint ou grupo de endpoints DEVE ter seu T justificado com base na criticidade e natureza da operação.

### 3.2 Faixas Recomendadas por Tipo de Operação

| Tipo de Operação | Limite T Recomendado | Justificativa |
|-----------------|---------------------|---------------|
| Leitura simples (GET saldo, status) | 150ms | Operação trivial, alta expectativa |
| Escrita transacional (POST transação) | 300ms | Envolve banco + validação |
| Listagem paginada (GET extrato) | 500ms | Query com range + serialização |
| Operação complexa (relatórios, exports) | 2000ms | Processamento pesado esperado |
| Chamada inter-serviço (Produto → Ledger) | 200ms | Rede interna + transação ACID |

### 3.3 Aprovação do Limite T

- Cada time DEVE declarar o Limite T de suas APIs na documentação do serviço.
- Limites acima de 500ms para operações de leitura DEVEM ser justificados e aprovados pelo time de arquitetura.
- O Limite T deve ser revisado trimestralmente com base nos dados reais de latência.

---

## 4. Implementação Técnica (AWS Free Tier)

### 4.1 Fonte de Dados: Access Logs do API Gateway

- O API Gateway HTTP DEVE ter access logging habilitado em formato JSON.
- O log DEVE incluir obrigatoriamente: `$context.requestId`, `$context.routeKey`, `$context.responseLatency`, `$context.status`, `$context.requestTime`.
- O destino dos access logs é o CloudWatch Log Group dedicado.

### 4.2 Cálculo do Apdex via CloudWatch Logs Insights

O Apdex é calculado via query no CloudWatch Logs Insights (gratuito até 5GB/mês de scan):

```sql
filter status < 500 or status >= 500
| stats
    count(*) as total,
    sum(case when responseLatency <= @T and status < 500 then 1 else 0 end) as satisfied,
    sum(case when responseLatency > @T and responseLatency <= @T * 4 and status < 500 then 1 else 0 end) as tolerating
| eval apdex = (satisfied + tolerating * 0.5) / total
```

> Onde `@T` é substituído pelo Limite T da API em milissegundos.

### 4.3 Custom Metrics via Embedded Metric Format (EMF)

Para ter métricas em tempo real no CloudWatch sem custo adicional, a aplicação .NET DEVE emitir métricas no formato EMF através dos logs estruturados:

```csharp
// Middleware de categorização Apdex
public class ApdexMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        await _next(context);
        sw.Stop();

        var latencyMs = sw.ElapsedMilliseconds;
        var limitT = GetLimitT(context.Request.Path); // Configurável por rota
        
        var category = context.Response.StatusCode >= 500 ? "frustrated"
            : latencyMs <= limitT ? "satisfied"
            : latencyMs <= limitT * 4 ? "tolerating"
            : "frustrated";

        // Emite métrica EMF no log (CloudWatch cria custom metric automaticamente)
        _logger.LogInformation(
            new { 
                _aws = new { 
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    CloudWatchMetrics = new[] { new {
                        Namespace = "CorePoints/Apdex",
                        Dimensions = new[] { new[] { "ServiceName", "Route", "Category" } },
                        Metrics = new[] { 
                            new { Name = "RequestCount", Unit = "Count" },
                            new { Name = "Latency", Unit = "Milliseconds" }
                        }
                    }}
                },
                ServiceName = "ledger-api",
                Route = context.Request.Path.Value,
                Category = category,
                RequestCount = 1,
                Latency = latencyMs
            }.ToJson());
    }
}
```

### 4.4 Dashboard CloudWatch (Gratuito)

- Um CloudWatch Dashboard centralizado DEVE ser provisionado via Terraform.
- O dashboard usa Metric Math para calcular o Apdex a partir das custom metrics EMF.
- Limite de Free Tier: 3 dashboards com até 50 widgets cada.

### 4.5 Alarmes CloudWatch

- Um CloudWatch Alarm DEVE ser criado para cada API com threshold: Apdex < 0.85 por 5 minutos.
- O alarme publica no SNS topic existente para notificação.
- Limite de Free Tier: 10 alarms (suficiente para as APIs iniciais).

---

## 5. Estrutura do Dashboard Obrigatório

### 5.1 Painel Geral (Tabela Consolidada)

Todas as APIs devem aparecer em uma tabela com:

| Coluna | Descrição |
|--------|-----------|
| Nome da API / Serviço | Identificação do microserviço |
| Apdex Atual | Nota de 0 a 1 com código de cores |
| Limite T | Tempo alvo configurado (ms) |
| Volume de Requisições | Total no período avaliado |
| Taxa de Erro 5xx | Percentual de erros internos |

### 5.2 Visão de Detalhes (Drill-Down por API)

Ao investigar uma API com Apdex degradado, o painel deve exibir:

1. **Gráfico de Linha — Apdex no Tempo**: Tendência dos últimos 7 dias (identificar degradação gradual vs. pico)
2. **Gráfico de Barras Empilhadas — Categorias**: Satisfied, Tolerating, Frustrated por intervalo de 1 minuto
3. **Gráfico de Taxa de Erro 5xx**: Para diferenciar lentidão geral de falhas no servidor

### 5.3 Alertas Automatizados

| Condição | Ação | Canal |
|----------|------|-------|
| Apdex < 0.85 por 5 min | Alerta WARN | SNS → Slack/Teams |
| Apdex < 0.70 por 3 min | Alerta CRITICAL | SNS → PagerDuty/On-call |
| Taxa 5xx > 5% por 2 min | Alerta ERROR | SNS → Slack/Teams |

---

## 6. Regras para Código Gerado

### 6.1 Middleware Obrigatório

- Toda API .NET DEVE incluir o middleware de categorização Apdex (conforme seção 4.3).
- O middleware DEVE ser registrado ANTES dos middlewares de autenticação e roteamento.
- O Limite T DEVE ser configurável via `appsettings.json` ou SSM Parameter Store (não hardcoded).

### 6.2 Exclusões do Cálculo

As seguintes requisições NÃO entram no cálculo do Apdex:
- Health checks (`/health/live`, `/health/ready`)
- Endpoints de métricas/diagnóstico (`/metrics`, `/swagger`)
- Requisições com status 4xx (erros de validação do cliente)

### 6.3 Configuração do Limite T

```json
// appsettings.json
{
  "Apdex": {
    "DefaultLimitMs": 300,
    "Routes": {
      "GET /accounts/{id}/balance": 150,
      "POST /transactions": 300,
      "GET /accounts/{id}/statement": 500
    }
  }
}
```

---

## 7. Infraestrutura Necessária (Terraform)

| Recurso | Módulo | Descrição |
|---------|--------|-----------|
| CloudWatch Log Group (access logs) | compute | `/apigateway/{env}-access-logs` |
| API Gateway Access Logging | compute | Formato JSON com responseLatency |
| CloudWatch Dashboard | observability (novo) | Dashboard centralizado Apdex |
| CloudWatch Alarms | observability (novo) | 1 alarm por API (threshold 0.85) |
| SNS Topic (alertas) | existente | Reutiliza `events-topic` ou cria dedicado |

---

## 8. Compliance e Revisão

- Todo novo serviço publicado no API Gateway DEVE ter Apdex monitorado antes de ir para produção.
- A revisão trimestral de Limites T é obrigatória — times devem apresentar evidências de que o T está alinhado com a realidade dos percentis p95/p99.
- APIs com Apdex abaixo de 0.85 por mais de 1 semana DEVEM ter um plano de ação registrado.
