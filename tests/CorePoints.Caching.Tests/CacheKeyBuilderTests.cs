using FluentAssertions;

namespace CorePoints.Caching.Tests;

public class CacheKeyBuilderTests
{
    [Fact]
    public void Build_WithValidComponents_ReturnsFormattedKey()
    {
        var key = CacheKeyBuilder.Build("ledger", "balance", "acc-123");
        key.Should().Be("ledger:balance:acc-123");
    }

    [Theory]
    [InlineData("", "entity", "id")]
    [InlineData("service", "", "id")]
    [InlineData("service", "entity", "")]
    [InlineData(null, "entity", "id")]
    [InlineData("service", null, "id")]
    [InlineData("service", "entity", null)]
    public void Build_WithEmptyOrNullComponent_ThrowsArgumentException(string? service, string? entity, string? id)
    {
        var act = () => CacheKeyBuilder.Build(service!, entity!, id!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("ser vice", "entity", "id")]
    [InlineData("service", "ent ity", "id")]
    [InlineData("service", "entity", "i d")]
    public void Build_WithWhitespace_ThrowsArgumentException(string service, string entity, string id)
    {
        var act = () => CacheKeyBuilder.Build(service, entity, id);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("ser:vice", "entity", "id")]
    [InlineData("service", "ent:ity", "id")]
    [InlineData("service", "entity", "i:d")]
    public void Build_WithColon_ThrowsArgumentException(string service, string entity, string id)
    {
        var act = () => CacheKeyBuilder.Build(service, entity, id);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Service", "entity", "id")]
    [InlineData("service", "Entity", "id")]
    [InlineData("service", "entity", "ID")]
    public void Build_WithUppercase_ThrowsArgumentException(string service, string entity, string id)
    {
        var act = () => CacheKeyBuilder.Build(service, entity, id);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LedgerBalance_ReturnsCorrectKey()
    {
        var key = CacheKeyBuilder.LedgerBalance("acc-456");
        key.Should().Be("ledger:balance:acc-456");
    }

    [Fact]
    public void ProductData_ReturnsCorrectKey()
    {
        var key = CacheKeyBuilder.ProductData("prod-789");
        key.Should().Be("product:data:prod-789");
    }
}
