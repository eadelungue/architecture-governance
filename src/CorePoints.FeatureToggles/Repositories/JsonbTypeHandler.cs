using System.Data;
using System.Text.Json;
using Dapper;

namespace CorePoints.FeatureToggles.Repositories;

/// <summary>
/// Dapper type handler that maps List&lt;string&gt; to/from PostgreSQL JSONB columns.
/// </summary>
public sealed class JsonbTypeHandler : SqlMapper.TypeHandler<List<string>>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override void SetValue(IDbDataParameter parameter, List<string>? value)
    {
        parameter.Value = JsonSerializer.Serialize(value ?? new List<string>(), JsonOptions);
        parameter.DbType = DbType.String;
    }

    public override List<string> Parse(object value)
    {
        if (value is string json)
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
        }

        return new List<string>();
    }
}
