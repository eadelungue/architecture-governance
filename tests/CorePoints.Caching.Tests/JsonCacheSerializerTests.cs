using CorePoints.Caching.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CorePoints.Caching.Tests;

public class JsonCacheSerializerTests
{
    private readonly JsonCacheSerializer _serializer = new(NullLogger<JsonCacheSerializer>.Instance);

    private record TestPoco(string Name, int Age, bool IsActive);

    [Fact]
    public void RoundTrip_Serialization_PreservesValues()
    {
        var original = new TestPoco("Alice", 30, true);

        var bytes = _serializer.Serialize(original);
        var deserialized = _serializer.Deserialize<TestPoco>(bytes);

        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("Alice");
        deserialized.Age.Should().Be(30);
        deserialized.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var value = new TestPoco("Bob", 25, false);

        var bytes = _serializer.Serialize(value);
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        json.Should().Contain("\"name\":");
        json.Should().Contain("\"age\":");
        json.Should().Contain("\"isActive\":");
        json.Should().NotContain("\"Name\":");
        json.Should().NotContain("\"Age\":");
        json.Should().NotContain("\"IsActive\":");
    }

    [Fact]
    public void Serialize_ProducesNoIndentation()
    {
        var value = new TestPoco("Charlie", 40, true);

        var bytes = _serializer.Serialize(value);
        var json = System.Text.Encoding.UTF8.GetString(bytes);

        json.Should().NotContain("\n");
        json.Should().NotContain("\r");
    }

    [Fact]
    public void Deserialize_CorruptedData_ReturnsNull()
    {
        var corruptedBytes = System.Text.Encoding.UTF8.GetBytes("this is not valid json{{{");

        var result = _serializer.Deserialize<TestPoco>(corruptedBytes);

        result.Should().BeNull();
    }

    [Fact]
    public void Deserialize_EmptyArray_ReturnsNull()
    {
        var emptyBytes = Array.Empty<byte>();

        var result = _serializer.Deserialize<TestPoco>(emptyBytes);

        result.Should().BeNull();
    }
}
