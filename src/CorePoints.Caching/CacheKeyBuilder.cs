using System.Text.RegularExpressions;

namespace CorePoints.Caching;

/// <summary>
/// Static utility class for building cache keys following the convention {service}:{entity}:{id}.
/// </summary>
public static partial class CacheKeyBuilder
{
    private static readonly Regex ValidComponentRegex = GeneratedValidComponentRegex();

    /// <summary>
    /// Builds a cache key in format {service}:{entity}:{id}.
    /// All components must be non-empty, lowercase, and free of whitespace or colons.
    /// </summary>
    /// <param name="service">The service name (e.g., "ledger").</param>
    /// <param name="entity">The entity type (e.g., "balance").</param>
    /// <param name="id">The entity identifier.</param>
    /// <returns>A formatted cache key string.</returns>
    /// <exception cref="ArgumentException">Thrown when any component is invalid.</exception>
    public static string Build(string service, string entity, string id)
    {
        ValidateComponent(service, nameof(service));
        ValidateComponent(entity, nameof(entity));
        ValidateComponent(id, nameof(id));

        return $"{service}:{entity}:{id}";
    }

    /// <summary>
    /// Builds a cache key for a ledger balance entry.
    /// </summary>
    public static string LedgerBalance(string accountId) => Build("ledger", "balance", accountId);

    /// <summary>
    /// Builds a cache key for a product data entry.
    /// </summary>
    public static string ProductData(string productId) => Build("product", "data", productId);

    private static void ValidateComponent(string value, string paramName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("Cache key component cannot be null or empty.", paramName);
        }

        if (!ValidComponentRegex.IsMatch(value))
        {
            throw new ArgumentException(
                $"Cache key component '{value}' contains invalid characters. Only lowercase alphanumeric characters and hyphens are allowed.",
                paramName);
        }
    }

    [GeneratedRegex(@"^[a-z0-9\-]+$", RegexOptions.Compiled)]
    private static partial Regex GeneratedValidComponentRegex();
}
