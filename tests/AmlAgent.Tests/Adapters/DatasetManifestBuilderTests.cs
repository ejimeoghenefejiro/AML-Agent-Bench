using System.Text.Json.Nodes;
using AmlAgent.Adapters;
using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Formats;
using AmlAgent.Adapters.Manifest;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class DatasetManifestBuilderTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* best effort */ }
    }

    private string WriteTempCsv(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aml-manifest-test-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private const string ValidCsv =
        "transaction_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked\n" +
        "T1,N001,N002,4500.00,USD,2026-01-19T10:00:00Z,wire,US,true\n";

    [Fact]
    public async Task BuildAsync_FileBasedSource_PopulatesRealDatasetHash()
    {
        var path = WriteTempCsv(ValidCsv);
        var adapter = new CsvDataAdapter();
        var source = new DataSourceConfiguration("csv", Path: path);
        var dataset = await adapter.LoadAsync(source);

        var manifest = await DatasetManifestBuilder.BuildAsync(source, adapter, dataset, DateTimeOffset.UtcNow);

        Assert.NotNull(manifest["dataset_hash"]);
        Assert.StartsWith("sha256:", manifest["dataset_hash"]!.GetValue<string>());
        Assert.Null(manifest["snapshot_limitation"]);
    }

    [Fact]
    public async Task BuildAsync_LiveSourceWithNoLocalFile_RecordsLimitationInsteadOfFakingHash()
    {
        var adapter = new CsvDataAdapter(); // adapter identity doesn't matter here, only source.Path absence
        var source = new DataSourceConfiguration("postgresql", ConnectionProfile: "some-profile");
        var dataset = CanonicalAmlDataset.Empty() with
        {
            Transactions = new[]
            {
                new CanonicalTransaction("T1", "N001", "N002", 100m, "USD", DateTimeOffset.UtcNow, "wire", "US", false,
                    new SourceLineage("postgresql", null, "transactions", "T1", "postgresql", "1.0.0"))
            }
        };

        var manifest = await DatasetManifestBuilder.BuildAsync(source, adapter, dataset, DateTimeOffset.UtcNow);

        Assert.Null(manifest["dataset_hash"]);
        Assert.NotNull(manifest["snapshot_limitation"]);
        Assert.Contains("postgresql", manifest["snapshot_limitation"]!.GetValue<string>());
        // normalisation_hash must still be real even when dataset_hash is unavailable.
        Assert.NotNull(manifest["normalisation_hash"]);
        Assert.StartsWith("sha256:", manifest["normalisation_hash"]!.GetValue<string>());
    }

    [Fact]
    public async Task BuildAsync_RecordCountMatchesDatasetTotal()
    {
        var path = WriteTempCsv(ValidCsv);
        var adapter = new CsvDataAdapter();
        var source = new DataSourceConfiguration("csv", Path: path);
        var dataset = await adapter.LoadAsync(source);

        var manifest = await DatasetManifestBuilder.BuildAsync(source, adapter, dataset, DateTimeOffset.UtcNow);

        Assert.Equal(dataset.TotalRecordCount, manifest["record_count"]!.GetValue<int>());
        Assert.Equal(1, manifest["record_counts_by_type"]!["transactions"]!.GetValue<int>());
    }

    [Fact]
    public async Task BuildAsync_RecordsAdapterIdentityAndSchemaVersion()
    {
        var path = WriteTempCsv(ValidCsv);
        var adapter = new CsvDataAdapter();
        var source = new DataSourceConfiguration("csv", Path: path);
        var dataset = await adapter.LoadAsync(source);

        var manifest = await DatasetManifestBuilder.BuildAsync(source, adapter, dataset, DateTimeOffset.UtcNow);

        Assert.Equal("csv", manifest["adapter"]!.GetValue<string>());
        Assert.Equal("1.0.0", manifest["adapter_version"]!.GetValue<string>());
        Assert.Equal(CanonicalSchema.Version, manifest["schema_version"]!.GetValue<string>());
        Assert.Equal("csv", manifest["source_type"]!.GetValue<string>());
    }

    [Fact]
    public async Task BuildAsync_DatasetIdIncludesSourceTypeAndTimestamp()
    {
        var path = WriteTempCsv(ValidCsv);
        var adapter = new CsvDataAdapter();
        var source = new DataSourceConfiguration("csv", Path: path);
        var dataset = await adapter.LoadAsync(source);
        var timestamp = new DateTimeOffset(2026, 1, 19, 10, 0, 0, TimeSpan.Zero);

        var manifest = await DatasetManifestBuilder.BuildAsync(source, adapter, dataset, timestamp);

        Assert.Equal("csv-20260119T100000Z", manifest["dataset_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Write_ProducesValidJsonFileReadableBack()
    {
        var path = WriteTempCsv(ValidCsv);
        var adapter = new CsvDataAdapter();
        var source = new DataSourceConfiguration("csv", Path: path);
        var dataset = await adapter.LoadAsync(source);
        var manifest = await DatasetManifestBuilder.BuildAsync(source, adapter, dataset, DateTimeOffset.UtcNow);

        var outPath = Path.Combine(Path.GetTempPath(), $"aml-manifest-out-{Guid.NewGuid():N}.json");
        _tempFiles.Add(outPath);
        DatasetManifestBuilder.Write(manifest, outPath);

        Assert.True(File.Exists(outPath));
        var reread = JsonNode.Parse(File.ReadAllText(outPath))!.AsObject();
        Assert.Equal(manifest["dataset_hash"]!.GetValue<string>(), reread["dataset_hash"]!.GetValue<string>());
    }

    [Fact]
    public async Task BuildAsync_TwoIdenticalRunsAgainstSameFile_ProduceSameNormalisationAndDatasetHash()
    {
        var path = WriteTempCsv(ValidCsv);
        var adapter = new CsvDataAdapter();
        var source = new DataSourceConfiguration("csv", Path: path);

        var dataset1 = await adapter.LoadAsync(source);
        var manifest1 = await DatasetManifestBuilder.BuildAsync(source, adapter, dataset1, DateTimeOffset.UtcNow);
        var dataset2 = await adapter.LoadAsync(source);
        var manifest2 = await DatasetManifestBuilder.BuildAsync(source, adapter, dataset2, DateTimeOffset.UtcNow);

        Assert.Equal(manifest1["dataset_hash"]!.GetValue<string>(), manifest2["dataset_hash"]!.GetValue<string>());
        Assert.Equal(manifest1["normalisation_hash"]!.GetValue<string>(), manifest2["normalisation_hash"]!.GetValue<string>());
    }
}
