# DIRETRIZES DE BANCO DE DADOS (DAPPER & CQRS)

> **Contexto arquitetural:** O Ledger Core possui banco de dados isolado (schema/instância dedicada). Nenhum serviço de produto acessa diretamente as tabelas do Ledger. Consulte `18-ledger-core-architecture.md` para detalhes da separação.

## 0. Isolamento de Bancos de Dados

- O **Ledger Core** possui seu próprio banco PostgreSQL (ou schema isolado). Suas tabelas são otimizadas para operações contábeis de alta performance.
- Cada **serviço de produto** pode ter seu próprio banco/schema para estado de negócio, configurações e tabela Outbox.
- É PROIBIDO qualquer serviço de produto fazer queries diretamente nas tabelas do Ledger. Toda consulta de saldo/extrato passa pela API síncrona do Ledger.
- Connection pooling (PgBouncer ou RDS Proxy) é OBRIGATÓRIO em produção para o banco do Ledger.

---

**REGRA ABSOLUTA (banco legado de produto):** Para serviços que operam sobre bancos legados, você está ESTRITAMENTE PROIBIDO de criar novas tabelas, adicionar colunas, alterar tipos de dados ou gerar arquivos de *Migration* (ex: Entity Framework Migrations). Trabalhe APENAS com o esquema fornecido. Para o Ledger Core (banco novo), o schema é gerenciado via migrations controladas pela equipe de plataforma.

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

Use transações explícitas sempre que um Command precisar alterar mais de uma tabela de forma atômica.

### 4.1 No Ledger Core (síncrono, sem Outbox)

O Ledger Core executa transações ACID puras. Não há tabela Outbox no Ledger.

```csharp
// ✅ Padrão para o Ledger Core — transação contábil pura
public async Task<TransactionResult> ExecuteTransactionAsync(LedgerCommand command, CancellationToken ct)
{
    using var connection = _connectionFactory.CreateConnection();
    connection.Open();
    using var transaction = connection.BeginTransaction();

    try
    {
        // 1. Lock da conta (serialização por conta)
        const string lockSql = @"
            SELECT balance FROM tb_accounts
            WHERE id = @AccountId FOR UPDATE";

        var currentBalance = await connection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(lockSql, new { command.AccountId }, transaction, cancellationToken: ct));

        // 2. Débito na conta origem
        const string debitSql = @"
            UPDATE tb_accounts SET balance = balance - @Amount WHERE id = @DebitAccountId";

        await connection.ExecuteAsync(
            new CommandDefinition(debitSql, command, transaction, cancellationToken: ct));

        // 3. Crédito na conta destino
        const string creditSql = @"
            UPDATE tb_accounts SET balance = balance + @Amount WHERE id = @CreditAccountId";

        await connection.ExecuteAsync(
            new CommandDefinition(creditSql, command, transaction, cancellationToken: ct));

        // 4. Registra lançamento contábil
        const string entrySql = @"
            INSERT INTO tb_ledger_entries (debit_account_id, credit_account_id, amount, idempotency_key, created_at)
            VALUES (@DebitAccountId, @CreditAccountId, @Amount, @IdempotencyKey, NOW())
            RETURNING id";

        var entryId = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(entrySql, command, transaction, cancellationToken: ct));

        transaction.Commit();
        return new TransactionResult(entryId, currentBalance - command.Amount);
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

### 4.2 Nos Serviços de Produto (com Outbox)

Serviços de produto usam Outbox para propagar eventos APÓS confirmar a operação no Ledger (ver `10-financial-transactions.md`).

```csharp
// ✅ Padrão para Serviços de Produto — com Outbox local
public async Task ProcessCashbackAsync(CashbackCommand command, CancellationToken ct)
{
    // 1. Chama o Ledger de forma síncrona
    var ledgerResult = await _ledgerClient.ExecuteTransactionAsync(new
    {
        DebitAccountId = command.MerchantAccountId,
        CreditAccountId = command.CustomerAccountId,
        Amount = command.CashbackAmount,
        IdempotencyKey = command.IdempotencyKey
    }, ct);

    // 2. Persiste evento Outbox no banco LOCAL do produto
    using var connection = _connectionFactory.CreateConnection();
    connection.Open();
    using var transaction = connection.BeginTransaction();

    try
    {
        const string outboxSql = @"
            INSERT INTO tb_outbox_events (aggregate_id, event_type, payload, created_at)
            VALUES (@AggregateId, @EventType, @Payload::jsonb, NOW())";

        await connection.ExecuteAsync(
            new CommandDefinition(outboxSql, new
            {
                AggregateId = command.CustomerAccountId,
                EventType = "CashbackCredited",
                Payload = JsonSerializer.Serialize(new { command.CustomerAccountId, command.CashbackAmount, ledgerResult.TransactionId })
            }, transaction, cancellationToken: ct));

        // 3. Atualiza estado local do produto se necessário
        const string statusSql = @"
            UPDATE tb_cashback_requests SET status = 'completed', ledger_tx_id = @TxId WHERE id = @RequestId";

        await connection.ExecuteAsync(
            new CommandDefinition(statusSql, new { TxId = ledgerResult.TransactionId, command.RequestId }, transaction, cancellationToken: ct));

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
