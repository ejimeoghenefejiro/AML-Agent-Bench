using System.Text;
using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Normalisation;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class CanonicalHashingTests
{
    private static SourceLineage Lineage(string id) => new("csv", "fixture.csv", null, id, "csv", "1.0.0");

    private static CanonicalTransaction Txn(string id, decimal amount = 100m, string account = "A1") => new(
        TransactionId: id,
        SourceAccount: account,
        DestinationAccount: "A2",
        Amount: amount,
        Currency: "USD",
        Timestamp: new DateTimeOffset(2026, 1, 19, 10, 0, 0, TimeSpan.Zero),
        Channel: "wire",
        Jurisdiction: "US",
        SarLinked: false,
        SourceLineage: Lineage(id));

    [Fact]
    public void ComputeDatasetHash_SameBytes_ProducesSameHash()
    {
        var bytes = Encoding.UTF8.GetBytes("transaction_id,amount\nT1,100\n");
        Assert.Equal(CanonicalHashing.ComputeDatasetHash(bytes), CanonicalHashing.ComputeDatasetHash((byte[])bytes.Clone()));
    }

    [Fact]
    public void ComputeDatasetHash_DifferentBytes_ProducesDifferentHash()
    {
        var a = Encoding.UTF8.GetBytes("T1,100");
        var b = Encoding.UTF8.GetBytes("T1,200");
        Assert.NotEqual(CanonicalHashing.ComputeDatasetHash(a), CanonicalHashing.ComputeDatasetHash(b));
    }

    [Fact]
    public void ComputeDatasetHash_HasSha256Prefix()
    {
        var hash = CanonicalHashing.ComputeDatasetHash(Encoding.UTF8.GetBytes("x"));
        Assert.StartsWith("sha256:", hash);
        Assert.Equal(71, hash.Length); // "sha256:" (7) + 64 hex chars
    }

    [Fact]
    public void ComputeDatasetHash_ChunkedOverload_MatchesConcatenatedSingleCall()
    {
        var chunk1 = Encoding.UTF8.GetBytes("hello ");
        var chunk2 = Encoding.UTF8.GetBytes("world");
        var whole = Encoding.UTF8.GetBytes("hello world");

        var chunked = CanonicalHashing.ComputeDatasetHash(new[] { chunk1, chunk2 });
        var single = CanonicalHashing.ComputeDatasetHash(whole);

        Assert.Equal(single, chunked);
    }

    [Fact]
    public void ComputeNormalisationHash_SameDataset_ProducesSameHash()
    {
        var dataset = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1"), Txn("T2") } };
        var hash1 = CanonicalHashing.ComputeNormalisationHash(dataset);
        var hash2 = CanonicalHashing.ComputeNormalisationHash(dataset);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeNormalisationHash_IsIndependentOfCollectionOrder()
    {
        var forward = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1"), Txn("T2"), Txn("T3") } };
        var reversed = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T3"), Txn("T2"), Txn("T1") } };

        Assert.Equal(CanonicalHashing.ComputeNormalisationHash(forward), CanonicalHashing.ComputeNormalisationHash(reversed));
    }

    [Fact]
    public void ComputeNormalisationHash_DifferentTransactionData_ProducesDifferentHash()
    {
        var a = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", amount: 100m) } };
        var b = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", amount: 200m) } };

        Assert.NotEqual(CanonicalHashing.ComputeNormalisationHash(a), CanonicalHashing.ComputeNormalisationHash(b));
    }

    [Fact]
    public void ComputeNormalisationHash_DifferentRecordCount_ProducesDifferentHash()
    {
        var one = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1") } };
        var two = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1"), Txn("T2") } };

        Assert.NotEqual(CanonicalHashing.ComputeNormalisationHash(one), CanonicalHashing.ComputeNormalisationHash(two));
    }

    [Fact]
    public void ComputeNormalisationHash_EmptyDataset_IsDeterministicAndStable()
    {
        var hash1 = CanonicalHashing.ComputeNormalisationHash(CanonicalAmlDataset.Empty());
        var hash2 = CanonicalHashing.ComputeNormalisationHash(CanonicalAmlDataset.Empty());
        Assert.Equal(hash1, hash2);
        Assert.StartsWith("sha256:", hash1);
    }

    [Fact]
    public void ComputeNormalisationHash_DifferentSchemaVersion_ProducesDifferentHash()
    {
        var v1 = CanonicalAmlDataset.Empty("aml-canonical-1.0");
        var v2 = CanonicalAmlDataset.Empty("aml-canonical-2.0");
        Assert.NotEqual(CanonicalHashing.ComputeNormalisationHash(v1), CanonicalHashing.ComputeNormalisationHash(v2));
    }

    [Fact]
    public void ComputeNormalisationHash_SameSourceSnapshot_ReloadedTwice_ProducesSameHash()
    {
        // Simulates re-running the same adapter against an unchanged source: two
        // independently-built but logically-identical datasets must hash identically.
        var run1 = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1"), Txn("T2") } };
        var run2 = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1"), Txn("T2") } };
        Assert.Equal(CanonicalHashing.ComputeNormalisationHash(run1), CanonicalHashing.ComputeNormalisationHash(run2));
    }
}
