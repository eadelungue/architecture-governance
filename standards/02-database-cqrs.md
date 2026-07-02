# DIRETRIZES DE BANCO DE DADOS (DAPPER & CQRS)

**REGRA ABSOLUTA:** O banco de dados é LEGADO e INTOCÁVEL. Você está ESTRITAMENTE PROIBIDO de criar novas tabelas, adicionar colunas, alterar tipos de dados ou gerar arquivos de *Migration* (ex: Entity Framework Migrations). Trabalhe APENAS com o esquema fornecido.

---

## 1. Padrão CQRS

- Separe fisicamente as operações de Leitura (Queries) das operações de Escrita (Commands).
- **Queries:** Retornam DTOs específicos da tela/necessidade. Não retornam entidades de domínio inteiras.
- **Commands:** Executam ações e não retornam dados da entidade (retornam apenas status de sucesso, IDs gerados ou erros de validação).

---

## 2. Acesso a Dados com Dapper — Queries (Leitura)

- Não utilize Entity Framework Core para consultas de negócio, utilize EXCLUSIVAMENTE Dapper.
- **SQL Injection:** SEMPRE utilize consultas parametrizadas do Dapper (`@Parametro`). NUNCA concatene strings SQL.

```csharp
// ✅ Padrão obrigatório para Queries
public async Task<UserDto?> GetUserByIdAsync(int id, CancellationToken ct)
{
    const string sql = @"
        SELECT id        AS Id,
               user_name AS UserName,
               email     AS Email
        FROM   tb_users
        WHERE  id = @Id";

    using var connection = _connectionFactory.CreateConnection();
    return await connection.QueryFirstOrDefaultAsync<UserDto>(
        new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
}
```

---

## 3. Acesso a Dados com Dapper — Commands (Escrita)

- Commands devem retornar apenas o ID gerado ou o número de linhas afetadas. Nunca re-consulte a entidade dentro do mesmo Command — deixe isso para a Query subsequente.
- Use `ExecuteAsync` para operações sem retorno e `ExecuteScalarAsync` para capturar IDs gerados.

```csharp
// ✅ Padrão obrigatório para Commands
public async Task<int> CreateOrderAsync(CreateOrderCommand command, CancellationToken ct)
{
    const string sql = @"
        INSERT INTO tb_orders (customer_id, total_amount, status, created_at)
        VALUES (@CustomerId, @TotalAmount, @Status, @CreatedAt)
        RETURNING id";

    using var connection = _connectionFactory.CreateConnection();
    return await connection.ExecuteScalarAsync<int>(
        new CommandDefinition(sql, command, cancellationToken: ct));
}
```

---

## 4. Transações Atômicas com Dapper

Use transações explícitas sempre que um Command precisar alterar mais de uma tabela de forma atômica. Isso é **obrigatório** no padrão Outbox (ver `10-financial-transactions.md`).

```csharp
// ✅ Padrão obrigatório para transações — usado no padrão Outbox
public async Task ProcessPaymentAsync(PaymentCommand command, CancellationToken ct)
{
    using var connection = _connectionFactory.CreateConnection();
    connection.Open();
    using var transaction = connection.BeginTransaction();

    try
    {
        // 1. Altera o estado da entidade principal
        const string updateSql = @"
            UPDATE tb_accounts
            SET    balance = balance - @Amount
            WHERE  id = @AccountId";

        await connection.ExecuteAsync(
            new CommandDefinition(updateSql, command, transaction, cancellationToken: ct));

        // 2. Persiste o evento de outbox na MESMA transação
        const string outboxSql = @"
            INSERT INTO tb_outbox_events (aggregate_id, event_type, payload, created_at)
            VALUES (@AggregateId, @EventType, @Payload::jsonb, NOW())";

        await connection.ExecuteAsync(
            new CommandDefinition(outboxSql, new
            {
                AggregateId = command.AccountId,
                EventType   = "PaymentProcessed",
                Payload     = JsonSerializer.Serialize(command)
            }, transaction, cancellationToken: ct));

        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

---

## 5. Gerenciamento de Conexão (`IDbConnectionFactory`)

- **Nunca** instancie `NpgsqlConnection` diretamente nos repositórios. Use sempre uma abstração `IDbConnectionFactory` injetada via DI.
- Registre a factory como `Scoped` no `Program.cs` para garantir uma conexão por request HTTP.
- Use sempre `using var connection` para garantir que a conexão retorna ao pool mesmo em caso de exceção.

```csharp
// Contrato
public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
}

// Implementação
public class NpgsqlConnectionFactory(IConfiguration config) : IDbConnectionFactory
{
    private readonly string _connectionString =
        config.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string não configurada.");

    public IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);
}

// Registro em Program.cs
builder.Services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();
```

---

## 6. Paginação

- Todo endpoint de listagem que use Dapper DEVE implementar paginação com `LIMIT` e `OFFSET`. Nunca retorne todos os registros de uma tabela.
- Limite máximo por página: **100 registros**. Se `pageSize` não for enviado, o padrão é **20**.

```csharp
// ✅ Padrão obrigatório para listagens paginadas
public async Task<IEnumerable<OrderDto>> GetOrdersAsync(int page, int pageSize, CancellationToken ct)
{
    pageSize = Math.Min(pageSize, 100); // Garante o teto máximo
    var offset = (page - 1) * pageSize;

    const string sql = @"
        SELECT id AS Id, total_amount AS TotalAmount, status AS Status
        FROM   tb_orders
        ORDER  BY created_at DESC
        LIMIT  @PageSize OFFSET @Offset";

    using var connection = _connectionFactory.CreateConnection();
    return await connection.QueryAsync<OrderDto>(
        new CommandDefinition(sql, new { PageSize = pageSize, Offset = offset }, cancellationToken: ct));
}
```

---

## 7. Boas Práticas de Performance

- **`SplitQuery` para relacionamentos 1-N:** Ao usar `QueryMultiple` ou `multi-mapping` do Dapper, prefira queries separadas para coleções grandes a evitar o produto cartesiano.
- **`CommandDefinition` com `CancellationToken`:** Todo acesso ao banco DEVE usar `CommandDefinition` passando o `CancellationToken` recebido do Controller/Handler. Nunca use o overload sem `ct`.
- **Índices:** Ao escrever queries com `WHERE` em campos não-chave, sempre consulte o DBA sobre a existência de índice. Nunca assuma que a query será performática sem evidência.
- **`buffered: false`:** Para queries que retornam grandes volumes de dados (relatórios, exports), use `buffered: false` no `QueryAsync` para fazer streaming e evitar carregamento total em memória.
