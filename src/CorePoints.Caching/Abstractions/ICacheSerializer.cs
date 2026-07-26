namespace CorePoints.Caching.Abstractions;

/// <summary>
/// Abstraction for cache value serialization and deserialization.
/// </summary>
public interface ICacheSerializer
{
    /// <summary>
    /// Serializes a value to a byte array.
    /// </summary>
    byte[] Serialize<T>(T value);

    /// <summary>
    /// Deserializes a byte array to the specified type.
    /// Returns null if deserialization fails.
    /// </summary>
    T? Deserialize<T>(byte[] data);
}
