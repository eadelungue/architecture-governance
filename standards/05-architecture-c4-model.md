# DIRETRIZES DE DESIGN ARQUITETURAL (C4 MODEL)
- **Representação Mental:** Ao propor novas arquiteturas ou documentar integrações, utilize a estrutura lógica do C4 Model.
- **Nível 1 (Contexto):** O sistema deve ser isolado. Identifique claramente atores externos e sistemas legados.
- **Nível 2 (Containers):** Separe claramente as responsabilidades entre o Frontend (React/SPA), o BFF/API Gateway, as APIs de Domínio (Microserviços C#) e os Bancos de Dados. O Frontend NUNCA fala diretamente com as APIs de domínio, apenas via BFF/Gateway.
- **Nível 3 (Componentes):** Dentro do C#, respeite a separação entre Controllers, Casos de Uso (Application) e Infraestrutura.