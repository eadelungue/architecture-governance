# DIRETRIZES DE BACKEND (C# / .NET)

## 1. Estrutura e Arquitetura
- O backend consiste em APIs REST, BFFs (Backend for Frontend) e AWS Lambdas escritos em C#.
- Mantenha a separação de responsabilidades: Controladores/Handlers chamam Casos de Uso (Application Layer). Casos de Uso chamam Repositórios (Infrastructure Layer).

## 2. Padrões de Código C#
- Utilize recursos modernos do C# (C# 12 / .NET 8 ou superior).
- Sempre prefira injeção de dependência estrita (`IServiceCollection`).
- **Programação Assíncrona:** Todos os acessos a I/O (Banco, APIs externas, Filas) DEVEM ser assíncronos (`async/await`). Nunca use `.Result` ou `.Wait()`.
- **AWS Lambdas:** Mantenha os handlers leves. Injete dependências no construtor da classe base para aproveitar a reutilização de contêineres e mitigar cold starts.

## 3. Gestão de Erros
- Nunca exponha *stack traces* para o cliente.
- Utilize o padrão RFC 7807 (Problem Details) para respostas de erro HTTP estruturadas.