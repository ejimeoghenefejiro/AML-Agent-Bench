using AmlAgent.Adapters.Canonical;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class CanonicalAmlCaseEvidenceExtensionsTests
{
    private static SourceLineage Lineage(string sourceType, string id) => new(sourceType, $"{sourceType}.file", null, id, sourceType, "1.0.0");

    [Fact]
    public void ToEvidenceReferences_Dataset_CoversAllTenRecordTypes()
    {
        var dataset = new CanonicalAmlDataset(
            CanonicalSchema.Version,
            Transactions: new[] { new CanonicalTransaction("T1", "A1", "A2", 100m, "USD", DateTimeOffset.UtcNow, "wire", "US", false, Lineage("csv", "T1")) },
            Accounts: new[] { new CanonicalAccount("ACC1", "owner", "bank", "USD", Lineage("csv", "ACC1")) },
            Customers: new[] { new CanonicalCustomer("CUST1", "name", "low", "US", Lineage("csv", "CUST1")) },
            Entities: new[] { new CanonicalEntity("E1", "Account", "E1", Lineage("graphml", "E1")) },
            Relationships: new[] { new CanonicalRelationship("R1", "E1", "E1", "transferred_to", Array.Empty<string>(), Lineage("graphml", "R1")) },
            Cases: new[] { new CanonicalCase("CASE1", "title", "open", Lineage("json", "CASE1")) },
            Alerts: new[] { new CanonicalAlert("ALERT1", "CASE1", "typology", "high", Lineage("json", "ALERT1")) },
            Evidence: new[] { new CanonicalEvidence("EV1", "document", "desc", Array.Empty<string>(), Lineage("json", "EV1")) },
            Jurisdictions: new[] { new CanonicalJurisdiction("US", "United States", false, Lineage("json", "US")) },
            Sars: new[] { new CanonicalSar("SAR1", "CASE1", Array.Empty<string>(), "narrative", Lineage("json", "SAR1")) });

        var refs = dataset.ToEvidenceReferences();

        Assert.Equal(10, refs.Count);
        Assert.Contains(refs, r => r.EvidenceId == "T1" && r.EvidenceType == "transaction");
        Assert.Contains(refs, r => r.EvidenceId == "ACC1" && r.EvidenceType == "account");
        Assert.Contains(refs, r => r.EvidenceId == "CUST1" && r.EvidenceType == "customer");
        Assert.Contains(refs, r => r.EvidenceId == "E1" && r.EvidenceType == "entity");
        Assert.Contains(refs, r => r.EvidenceId == "R1" && r.EvidenceType == "relationship");
        Assert.Contains(refs, r => r.EvidenceId == "CASE1" && r.EvidenceType == "case");
        Assert.Contains(refs, r => r.EvidenceId == "ALERT1" && r.EvidenceType == "alert");
        Assert.Contains(refs, r => r.EvidenceId == "EV1" && r.EvidenceType == "evidence");
        Assert.Contains(refs, r => r.EvidenceId == "US" && r.EvidenceType == "jurisdiction");
        Assert.Contains(refs, r => r.EvidenceId == "SAR1" && r.EvidenceType == "sar");
    }

    [Fact]
    public void ToEvidenceReferences_Dataset_RecordsSourceFromLineage()
    {
        var dataset = CanonicalAmlDataset.Empty() with
        {
            Relationships = new[] { new CanonicalRelationship("R1", "A", "B", "transferred_to", Array.Empty<string>(), Lineage("graphml", "R1")) },
        };

        var refs = dataset.ToEvidenceReferences();

        Assert.Equal("graphml", Assert.Single(refs).Source);
    }

    [Fact]
    public void ToEvidenceReferences_EmptyDataset_ReturnsEmpty()
    {
        Assert.Empty(CanonicalAmlDataset.Empty().ToEvidenceReferences());
    }

    [Fact]
    public void ToEvidenceReferences_Case_MergedFromMultipleSources_CoversAllTypes()
    {
        var merged = CanonicalCaseMerger.Merge(new[]
        {
            CanonicalAmlDataset.Empty() with { Transactions = new[] { new CanonicalTransaction("T1", "A1", "A2", 100m, "USD", DateTimeOffset.UtcNow, "wire", "US", false, Lineage("csv", "T1")) } },
            CanonicalAmlDataset.Empty() with
            {
                Entities = new[] { new CanonicalEntity("A1", "Account", "A1", Lineage("graphml", "A1")), new CanonicalEntity("A2", "Account", "A2", Lineage("graphml", "A2")) },
                Relationships = new[] { new CanonicalRelationship("R1", "A1", "A2", "transferred_to", new[] { "T1" }, Lineage("graphml", "R1")) },
            },
        });

        var refs = merged.ToEvidenceReferences();

        Assert.Contains(refs, r => r.EvidenceId == "T1" && r.EvidenceType == "transaction");
        Assert.Contains(refs, r => r.EvidenceId == "R1" && r.EvidenceType == "relationship");
        Assert.Equal(2, refs.Count(r => r.EvidenceType == "entity"));
    }

    [Fact]
    public void ToEvidenceReferences_CaseThenScoring_EndToEnd_RecognisesCrossSourceCitation()
    {
        // The end-to-end proof: a real merged multi-source case, converted to
        // EvidenceReferences, correctly grounds a report citing a relationship
        // id -- the exact scenario documented as broken before this fix.
        var merged = CanonicalCaseMerger.Merge(new[]
        {
            CanonicalAmlDataset.Empty() with
            {
                Entities = new[] { new CanonicalEntity("ACC-ALPHA", "Account", "Alpha", Lineage("graphml", "ACC-ALPHA")), new CanonicalEntity("ACC-BETA", "Account", "Beta", Lineage("graphml", "ACC-BETA")) },
                Relationships = new[] { new CanonicalRelationship("REL-001", "ACC-ALPHA", "ACC-BETA", "transferred_to", Array.Empty<string>(), Lineage("graphml", "REL-001")) },
            },
        });

        var evidenceUniverse = merged.ToEvidenceReferences();
        var result = EvidenceScoring.ComputeTraceability(
            "The transfer is confirmed by relationship edge REL-001 in the graph.",
            evidenceUniverse, evidenceUniverse);

        Assert.Contains("REL-001", result.GroundedCitations);
        Assert.Equal(1.0, result.Precision);
    }
}
