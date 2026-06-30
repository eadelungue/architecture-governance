# Índice de Governança (Fonte da Verdade)

Este arquivo define a ordem de precedência e a finalidade de cada documento de governança. O Kiro deve consultar estes documentos sempre que for realizar tarefas de design, implementação ou revisão de infraestrutura.

## 1. Prioridade de Precedência (Lei Superior)
1. **[00-global-rules.md](../standards/00-global-rules.md)**: Regras de conformidade, LGPD e Zero Trust lógico.
2. **[00-infrastructure-governance.md](../standards/00-infrastructure-governance.md)**: Regras obrigatórias de infraestrutura, custos (Free Tier) e segurança de perímetro (VPC/IAM). **Estas normas sobrepõem qualquer sugestão de código ou design.**
3. **[10-financial-transactions.md](../standards/10-financial-transactions.md)**: Normas para transações críticas e consistência financeira.
4. **[11-resilience.md](../standards/11-resilience.md)**: Políticas de tolerância a falhas.

## 2. Padrões de Implementação (Regras de Domínio)
* **[01-backend-csharp.md](../standards/01-backend-csharp.md)**: Padrões de C#/.NET e DDD.
* **[02-database-cqrs.md](../standards/02-database-cqrs.md)**: Acesso a dados (Dapper) e isolamento do legado.
* **[03-frontend-react.md](../standards/03-frontend-react.md)**: Frontend, Amplify e gestão de estado.
* **[04-testing.md](../standards/04-testing.md)**: Padrões de teste (AAA, Mocking).
* **[05-architecture-c4-model.md](../standards/05-architecture-c4-model.md)**: Representação arquitetural e System Design.
* **[06-api-gateway.md](../standards/06-api-gateway.md)**: Roteamento e segurança de borda.
* **[07-observability.md](../standards/07-observability.md)**: OpenTelemetry, Tracing e Health Checks.
* **[08-event-driven.md](../standards/08-event-driven.md)**: Mensageria e Idempotência.
* **[09-api-governance.md](../standards/09-api-governance.md)**: OpenAPI, Swagger e Contratos.
* **[12-caching.md](../standards/12-caching.md)**: Cache-Aside e Redis.
* **[13-feature-toggles.md](../standards/13-feature-toggles.md)**: Feature Flags e Release.

## 3. Instrução ao Kiro
"Ao iniciar qualquer tarefa, verifique a seção de prioridade acima. Em caso de dúvidas sobre qual regra aplicar, utilize o documento de maior prioridade na lista."