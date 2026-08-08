using AmlAgent.Adapters;
using AmlAgent.Adapters.Canonical;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class AdapterRegistryTests
{
    [Theory]
    [InlineData("csv", "csv")]
    [InlineData("json", "json")]
    [InlineData("jsonl", "json")] // jsonl is handled by JsonDataAdapter, whose own AdapterId is always "json"
    [InlineData("parquet", "parquet")]
    [InlineData("postgresql", "postgresql")]
    [InlineData("sqlserver", "sqlserver")]
    [InlineData("rest", "rest")]
    [InlineData("neo4j", "neo4j")]
    [InlineData("graphml", "graphml")]
    public void CreateDefault_ResolvesEachBuiltInSourceType(string sourceType, string expectedAdapterId)
    {
        var registry = AdapterRegistry.CreateDefault();
        var adapter = registry.Resolve(sourceType);
        Assert.Equal(expectedAdapterId, adapter.AdapterId);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var registry = AdapterRegistry.CreateDefault();
        Assert.Equal("csv", registry.Resolve("CSV").AdapterId);
        Assert.Equal("csv", registry.Resolve("Csv").AdapterId);
    }

    [Fact]
    public void Resolve_UnknownSourceType_ThrowsUnsupportedSourceTypeExceptionListingKnownTypes()
    {
        var registry = AdapterRegistry.CreateDefault();
        var ex = Assert.Throws<UnsupportedSourceTypeException>(() => registry.Resolve("xml-legacy"));
        Assert.Contains("xml-legacy", ex.Message);
        Assert.Contains("csv", ex.Message);
        Assert.Contains("neo4j", ex.Message);
    }

    [Fact]
    public void Resolve_EmptySourceType_ThrowsUnsupportedSourceTypeException()
    {
        var registry = AdapterRegistry.CreateDefault();
        Assert.Throws<UnsupportedSourceTypeException>(() => registry.Resolve(""));
    }

    [Fact]
    public void KnownSourceTypes_ContainsAllNineBuiltInAdapters()
    {
        var registry = AdapterRegistry.CreateDefault();
        var expected = new[] { "csv", "json", "jsonl", "parquet", "postgresql", "sqlserver", "rest", "neo4j", "graphml" };
        foreach (var type in expected)
            Assert.Contains(type, registry.KnownSourceTypes, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(expected.Length, registry.KnownSourceTypes.Count);
    }

    [Fact]
    public void Register_ExtendsRegistryWithoutModifyingCoreCode()
    {
        var registry = AdapterRegistry.CreateDefault();
        registry.Register("fake", () => new FakeAdapter());

        var adapter = registry.Resolve("fake");
        Assert.Equal("fake-adapter", adapter.AdapterId);
        Assert.Contains("fake", registry.KnownSourceTypes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Register_ReplacesExistingFactoryForSameSourceType()
    {
        var registry = AdapterRegistry.CreateDefault();
        registry.Register("csv", () => new FakeAdapter());

        Assert.Equal("fake-adapter", registry.Resolve("csv").AdapterId);
    }

    [Fact]
    public void Resolve_ReturnsFreshInstanceEachCall()
    {
        var registry = AdapterRegistry.CreateDefault();
        var first = registry.Resolve("csv");
        var second = registry.Resolve("csv");
        Assert.NotSame(first, second);
    }

    [Fact]
    public void DescribeRegisteredAdapters_ReportsIdAndVersionForEveryEntry_NoIO()
    {
        var registry = AdapterRegistry.CreateDefault();
        var descriptors = registry.DescribeRegisteredAdapters();

        Assert.Equal(9, descriptors.Count);
        var csv = descriptors.Single(d => d.SourceType == "csv");
        Assert.Equal("csv", csv.AdapterId);
        Assert.Equal("1.0.0", csv.AdapterVersion);
    }

    private sealed class FakeAdapter : IAmlDataAdapter
    {
        public string AdapterId => "fake-adapter";
        public string AdapterVersion => "0.0.1";
        public Task<CanonicalAmlDataset> LoadAsync(DataSourceConfiguration source, CancellationToken cancellationToken = default) =>
            Task.FromResult(CanonicalAmlDataset.Empty());
    }
}
