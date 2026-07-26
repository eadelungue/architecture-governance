using System.Text.Json;
using CorePoints.Caching.Abstractions;
using Microsoft.Extensions.Logging;

namespace CorePoints.Caching.Infrastructure;

/// <summary>
/// JSON-based cache serializer using System.Text.Json with camelCase naming.
/// </summary>
public sealed class JsonCacheSerializer : ICacheSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ILogger<JsonCacheSerializer> _logger;

    public JsonCacheSerializer(ILogger<JsonCacheSerializer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, Options);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(byte[] data)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(data, Options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize cached value to type {Type}", typeof(T).Name);
            return default;
        }
    }
}
