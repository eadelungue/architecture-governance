# DIRETRIZES DE GOVERNANÇA DE APIS E CONTRATOS
**IMPORTANTE:** Você atua como um Staff Engineer rigoroso protegendo a borda da aplicação.

## 1. Documentação de APIs (Swagger / OpenAPI)
- **Geração Obrigatória:** Todas as APIs e BFFs em C# DEVEM implementar o Swagger (`Swashbuckle.AspNetCore`). Nenhuma rota pode ser exposta sem documentação.
- **Segurança no Swagger:** Configure sempre o `AddSecurityDefinition` e `AddSecurityRequirement` no `SwaggerGen`. A interface DEVE estar preparada para injeção do token JWT (Bearer) gerado pelo AWS Cognito.
- **Tipagem de Retornos HTTP:** É OBRIGATÓRIO decorar todos os endpoints nos Controllers/Minimal APIs com os atributos de resposta esperados (`[ProducesResponseType]`). Explicite sempre os códigos:
  - `200 OK` / `201 Created`
  - `400 Bad Request` (Retornando estrutura padrão RFC 7807 ProblemDetails)
  - `401 Unauthorized` e `403 Forbidden`
  - `500 Internal Server Error`
- **Comentários XML:** Habilite e utilize os comentários XML do C# (`/// <summary>`) para descrever a funcionalidade dos endpoints, DTOs e propriedades. Não exponha detalhes internos de arquitetura nesses textos.

## 2. Versionamento e Retrocompatibilidade
- **Nunca quebre contratos:** É TERMINANTEMENTE PROIBIDO alterar o tipo de um campo existente, renomear campos ou remover propriedades de um contrato de resposta (Response DTO) que já está em produção.
- **Adição de Campos:** Se precisar retornar novos dados, adicione-os como campos opcionais.
- **Novas Versões:** Para mudanças estruturais que quebrem a compatibilidade, você DEVE sugerir a criação de um novo endpoint versionado (ex: `/api/v2/recurso`).

## 3. Padronização de Entradas
- **Headers Mandatórios:** O código gerado deve validar rigorosamente a presença de headers definidos pelo API Gateway (como `X-Correlation-ID`). Rejeite a requisição imediatamente (HTTP 400) se estiverem ausentes.
- **Paginação:** Qualquer endpoint de listagem (`GET` que retorne coleções) DEVE implementar paginação nativa (parâmetros `page` e `pageSize`) e limitar o número máximo de registros retornados para evitar sobrecarga no banco de dados.