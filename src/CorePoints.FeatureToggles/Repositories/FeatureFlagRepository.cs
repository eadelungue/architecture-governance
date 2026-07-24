using System.Data;
using CorePoints.FeatureToggles.Interfaces;
using CorePoints.FeatureToggles.Models;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace CorePoints.FeatureToggles.Repositories;

/// <summary>
/// Dapper-based repository for feature flags stored in PostgreSQL.
/// </summary>
public sealed class FeatureFlagRepository : IFeatureFlagRepository
{
    private readonly string _connectionString;

    public FeatureFlagRepository(IOptions<FeatureToggleOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task<FeatureFlag?> GetByNameAsync(string name, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, name, description, is_enabled AS IsEnabled, 
                   target_groups AS TargetGroups, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM feature_flags
            WHERE name = @Name
            """;

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, new { Name = name }, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<FeatureFlag>(command);
    }

    public async Task<IReadOnlyList<FeatureFlag>> GetAllAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, name, description, is_enabled AS IsEnabled, 
                   target_groups AS TargetGroups, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM feature_flags
            ORDER BY name
            """;

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
        var results = await connection.QueryAsync<FeatureFlag>(command);
        return results.AsList();
    }

    public async Task<FeatureFlag> CreateAsync(CreateFeatureFlagRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO feature_flags (name, description, is_enabled, target_groups, created_at, updated_at)
            VALUES (@Name, @Description, @IsEnabled, @TargetGroups::jsonb, NOW(), NOW())
            RETURNING id, name, description, is_enabled AS IsEnabled, 
                      target_groups AS TargetGroups, created_at AS CreatedAt, updated_at AS UpdatedAt
            """;

        var parameters = new
        {
            request.Name,
            request.Description,
            request.IsEnabled,
            TargetGroups = System.Text.Json.JsonSerializer.Serialize(request.TargetGroups ?? new List<string>())
        };

        await using var connection = CreateConnection();

        try
        {
            var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
            return await connection.QuerySingleAsync<FeatureFlag>(command);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException(
                $"A feature flag with name '{request.Name}' already exists.", ex);
        }
    }

    public async Task<FeatureFlag?> UpdateAsync(string name, UpdateFeatureFlagRequest request, CancellationToken cancellationToken)
    {
        // Build dynamic SET clause based on provided (non-null) fields
        var setClauses = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("Name", name);

        if (request.Description is not null)
        {
            setClauses.Add("description = @Description");
            parameters.Add("Description", request.Description);
        }

        if (request.IsEnabled is not null)
        {
            setClauses.Add("is_enabled = @IsEnabled");
            parameters.Add("IsEnabled", request.IsEnabled.Value);
        }

        if (request.TargetGroups is not null)
        {
            setClauses.Add("target_groups = @TargetGroups::jsonb");
            parameters.Add("TargetGroups", System.Text.Json.JsonSerializer.Serialize(request.TargetGroups));
        }

        // Always update updated_at
        setClauses.Add("updated_at = NOW()");

        if (setClauses.Count == 1)
        {
            // Only updated_at — still perform the update to refresh the timestamp
        }

        var sql = $"""
            UPDATE feature_flags
            SET {string.Join(", ", setClauses)}
            WHERE name = @Name
            RETURNING id, name, description, is_enabled AS IsEnabled, 
                      target_groups AS TargetGroups, created_at AS CreatedAt, updated_at AS UpdatedAt
            """;

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<FeatureFlag>(command);
    }

    public async Task<bool> DeleteAsync(string name, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM feature_flags WHERE name = @Name";

        await using var connection = CreateConnection();
        var command = new CommandDefinition(sql, new { Name = name }, cancellationToken: cancellationToken);
        var affectedRows = await connection.ExecuteAsync(command);
        return affectedRows > 0;
    }
}
