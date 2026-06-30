# DIRETRIZES DE FEATURE FLAGS E RELEASE

## 1. Gestão de Fluxo
- Toda nova funcionalidade deve estar protegida por uma *Feature Flag*.
- O estado da flag deve ser externo ao código (ex: AWS AppConfig ou uma tabela simples no banco via Dapper).

## 2. Padrão de Implementação
- A flag deve ser verificada na camada de *Use Case*. Nunca exponha a lógica de "se a flag está ligada" para o Frontend.
- **Canary Release:** Sempre que possível, configure as flags por grupos de usuários (ex: apenas colaboradores internos) antes de liberar para a base total.