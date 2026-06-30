# DIRETRIZES DE TESTES

## 1. Padrão OBRIGATÓRIO de Testes Unitários
- Siga rigorosamente a estrutura AAA (Arrange, Act, Assert). Coloque comentários separando cada fase no código gerado.
- Não teste a infraestrutura (banco de dados real ou AWS Cognito real) nos testes unitários.

## 2. Mocking (C#)
- Utilize frameworks de Mocking aprovados (ex: `Moq` ou `NSubstitute`).
- Sempre faça mock da `IDbConnection` ou das `Interfaces` de repositório ao testar a camada de *Application/Use Cases*.

## 3. Nomenclatura de Testes
- Siga o padrão: `MetodoSendoTestado_Cenario_ResultadoEsperado`.
  - Exemplo: `ProcessPayment_WithValidPayload_ReturnsSuccess`