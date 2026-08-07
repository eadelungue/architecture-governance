# DIRETRIZES DE TESTES

## 0. Regra Mandatória para Regras de Negócio
- Toda implementação ou alteração de regra de negócio DEVE incluir testes de regra de negócio entregues no mesmo ciclo de mudança.
- É proibido considerar concluída uma mudança de regra de negócio sem ao menos um teste automatizado cobrindo a decisão principal introduzida ou alterada.
- Sempre que possível, os testes de regra de negócio DEVEM ser escritos na camada que decide a regra, preferencialmente Domain ou Application.
- Quando a regra de negócio também altera contrato HTTP, status code, mensagem de erro ou payload, DEVEM existir testes de contrato/API complementares além dos testes unitários de negócio.
- Se a mudança atingir múltiplas regras no mesmo fluxo, os testes DEVEM cobrir pelo menos: caminho feliz, violação principal, caso de borda relevante e efeito esperado da rejeição.

## 1. Padrão OBRIGATÓRIO de Testes Unitários
- Siga rigorosamente a estrutura AAA (Arrange, Act, Assert). Coloque comentários separando cada fase no código gerado.
- Não teste a infraestrutura (banco de dados real ou AWS Cognito real) nos testes unitários.
- Testes unitários de regra de negócio devem validar comportamento observável e decisão da regra, não detalhes internos de implementação.

## 2. Mocking (C#)
- Utilize frameworks de Mocking aprovados (ex: `Moq` ou `NSubstitute`).
- Sempre faça mock da `IDbConnection` ou das `Interfaces` de repositório ao testar a camada de *Application/Use Cases*.

## 3. Nomenclatura de Testes
- Siga o padrão: `MetodoSendoTestado_Cenario_ResultadoEsperado`.
  - Exemplo: `ProcessPayment_WithValidPayload_ReturnsSuccess`

## 4. Critério de Conclusão
- Nenhuma task que altere regra de negócio pode ser marcada como concluída sem evidência de teste automatizado executado com sucesso.
- Em fluxos guiados por spec (`requirements.md`, `design.md`, `tasks.md`), o `tasks.md` DEVE conter tarefas explícitas para testes de regra de negócio antes da validação final.