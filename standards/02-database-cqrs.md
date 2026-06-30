# DIRETRIZES DE BANCO DE DADOS (DAPPER & CQRS)
**REGRA ABSOLUTA:** O banco de dados é LEGADO e INTOCÁVEL. Você está ESTRITAMENTE PROIBIDO de criar novas tabelas, adicionar colunas, alterar tipos de dados ou gerar arquivos de *Migration* (ex: Entity Framework Migrations). Trabalhe APENAS com o esquema fornecido.

## 1. Padrão CQRS
- Separe fisicamente as operações de Leitura (Queries) das operações de Escrita (Commands).
- **Queries:** Retornam DTOs específicos da tela/necessidade. Não retornam entidades de domínio inteiras.
- **Commands:** Executam ações e não retornam dados da entidade (retornam apenas status de sucesso, IDs gerados ou erros de validação).

## 2. Acesso a Dados com Dapper
- Não utilize Entity Framework Core para consultas de negócio, utilize EXCLUSIVAMENTE Dapper.
- **SQL Injection:** SEMPRE utilize consultas parametrizadas do Dapper (`@Parametro`). NUNCA concatene strings SQL.
- **Exemplo de Padrão Exigido:**
  ```csharp
  // Padrão obrigatório para Queries
  public async Task<UserDto> GetUserByIdAsync(int id)
  {
      const string sql = "SELECT id as Id, user_name as UserName, email as Email FROM tb_users WHERE id = @Id";
      using var connection = _connectionFactory.CreateConnection();
      return await connection.QueryFirstOrDefaultAsync<UserDto>(sql, new { Id = id });
  }