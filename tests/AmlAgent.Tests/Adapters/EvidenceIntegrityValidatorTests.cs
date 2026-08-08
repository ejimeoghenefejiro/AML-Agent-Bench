using AmlAgent.Adapters.Canonical;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class EvidenceIntegrityValidatorTests
{
    private static SourceLineage Lineage(string sourceType, string id, string adapter = "graphml") =>
        new(sourceType, $"{sourceType}-file", null, id, adapter, "1.0.0");

    private static CanonicalTransaction Txn(string id, string sourceType = "csv") => new(
        TransactionId: id, SourceAccount: "A1", DestinationAccount: "A2", Amount: 100m, Currency: "USD",
        Timestamp: DateTimeOffset.UtcNow, Channel: "wire", Jurisdiction: "US", SarLinked: false,
        SourceLineage: Lineage(sourceType, id, "csv"));

    private static CanonicalEvidence Evidence(string id, string sourceType = "json") => new(
        EvidenceId: id, EvidenceType: "document", Description: "desc", RelatedRecordIds: Array.Empty<string>(),
        SourceLineage: Lineage(sourceType, id, "json"));

    private static CanonicalRelationship Rel(string id, IReadOnlyList<string> evidenceIds, string sourceType = "graphml") => new(
        RelationshipId: id, SourceEntityId: "A100", TargetEntityId: "A200", RelationshipType: "transferred_to",
        EvidenceIds: evidenceIds, SourceLineage: Lineage(sourceType, id));

    private static CanonicalEntity Entity(string id) => new(id, "Account", id, Lineage("graphml", id));

    private static CanonicalSar Sar(string id, IReadOnlyList<string> txnIds, string sourceType = "csv") => new(
        SarId: id, CaseId: null, TransactionIds: txnIds, Narrative: "narrative", SourceLineage: Lineage(sourceType, id, "csv"));

    private static CanonicalAmlCase CaseWith(
        IReadOnlyList<CanonicalTransaction>? transactions = null,
        IReadOnlyList<CanonicalEntity>? entities = null,
        IReadOnlyList<CanonicalRelationship>? relationships = null,
        IReadOnlyList<CanonicalEvidence>? evidence = null,
        IReadOnlyList<CanonicalSar>? sars = null,
        IReadOnlyList<MergeConflict>? conflicts = null) => new(
        CanonicalSchema.Version,
        transactions ?? Array.Empty<CanonicalTransaction>(),
        Array.Empty<CanonicalAccount>(),
        Array.Empty<CanonicalCustomer>(),
        entities ?? Array.Empty<CanonicalEntity>(),
        relationships ?? Array.Empty<CanonicalRelationship>(),
        Array.Empty<CanonicalCase>(),
        Array.Empty<CanonicalAlert>(),
        evidence ?? Array.Empty<CanonicalEvidence>(),
        Array.Empty<CanonicalJurisdiction>(),
        sars ?? Array.Empty<CanonicalSar>(),
        conflicts ?? Array.Empty<MergeConflict>(),
        Array.Empty<SourceManifestEntry>());

    [Fact]
    public void Validate_EmptyCase_Passes()
    {
        var result = EvidenceIntegrityValidator.Validate(CaseWith());
        Assert.True(result.Passed);
        Assert.Equal("passed", result.Status);
    }

    [Fact]
    public void Validate_RelationshipEvidenceIdResolvesToRealEvidenceRecord_Passes()
    {
        var amlCase = CaseWith(
            evidence: new[] { Evidence("EV1") },
            relationships: new[] { Rel("R1", new[] { "EV1" }) });

        var result = EvidenceIntegrityValidator.Validate(amlCase);
        Assert.True(result.Passed);
    }

    [Fact]
    public void Validate_RelationshipEvidenceIdResolvesToTransaction_Passes()
    {
        // Relationships routinely cite a transaction id directly as evidence (no separate Evidence record).
        var amlCase = CaseWith(
            transactions: new[] { Txn("T10021") },
            relationships: new[] { Rel("R-1001", new[] { "T10021" }) });

        var result = EvidenceIntegrityValidator.Validate(amlCase);
        Assert.True(result.Passed);
    }

    [Fact]
    public void Validate_DanglingEvidenceReference_Fails_MatchesReportedShape()
    {
        var amlCase = CaseWith(relationships: new[] { Rel("R-1001", new[] { "T99999" }) });

        var result = EvidenceIntegrityValidator.Validate(amlCase);

        Assert.False(result.Passed);
        Assert.Equal("failed", result.Status);
        var issue = Assert.Single(result.DanglingReferences);
        Assert.Equal("relationship", issue.ReferencingRecordType);
        Assert.Equal("R-1001", issue.ReferencingRecordId);
        Assert.Equal("T99999", issue.EvidenceId);
    }

    [Fact]
    public void Validate_DanglingReference_PreservesReferencingSourceLineage()
    {
        var amlCase = CaseWith(relationships: new[] { Rel("R-1001", new[] { "T99999" }, "graphml") });

        var result = EvidenceIntegrityValidator.Validate(amlCase);

        var issue = Assert.Single(result.DanglingReferences);
        Assert.NotNull(issue.ReferencingSourceLineage);
        Assert.Equal("graphml", issue.ReferencingSourceLineage!.SourceType);
    }

    [Fact]
    public void Validate_EvidenceRelatedRecordIdDangling_ReportedAsDangling()
    {
        var dangling = new CanonicalEvidence("EV1", "document", "desc", new[] { "EV-MISSING" }, Lineage("json", "EV1", "json"));
        var amlCase = CaseWith(evidence: new[] { dangling });

        var result = EvidenceIntegrityValidator.Validate(amlCase);

        var issue = Assert.Single(result.DanglingReferences);
        Assert.Equal("evidence", issue.ReferencingRecordType);
        Assert.Equal("EV1", issue.ReferencingRecordId);
        Assert.Equal("EV-MISSING", issue.EvidenceId);
    }

    [Fact]
    public void Validate_SarReferencesMissingTransaction_ReportedAsMissingTransactionReference()
    {
        var amlCase = CaseWith(sars: new[] { Sar("SAR1", new[] { "T-GHOST" }) });

        var result = EvidenceIntegrityValidator.Validate(amlCase);

        Assert.False(result.Passed);
        var issue = Assert.Single(result.MissingTransactionReferences);
        Assert.Equal("sar", issue.ReferencingRecordType);
        Assert.Equal("SAR1", issue.ReferencingRecordId);
        Assert.Equal("T-GHOST", issue.EvidenceId);
    }

    [Fact]
    public void Validate_SarReferencesRealTransaction_Passes()
    {
        var amlCase = CaseWith(
            transactions: new[] { Txn("T1") },
            sars: new[] { Sar("SAR1", new[] { "T1" }) });

        var result = EvidenceIntegrityValidator.Validate(amlCase);
        Assert.True(result.Passed);
    }

    [Fact]
    public void Validate_EvidenceIdResolvesToWrongRecordType_ReportedAsIncompatible()
    {
        // "EV1" collides with an entity id from a different source -- exists in the
        // case, but not as evidence or a transaction.
        var amlCase = CaseWith(
            entities: new[] { Entity("EV1") },
            relationships: new[] { Rel("R1", new[] { "EV1" }) });

        var result = EvidenceIntegrityValidator.Validate(amlCase);

        Assert.False(result.Passed);
        var issue = Assert.Single(result.IncompatibleEvidenceTypes);
        Assert.Equal("EV1", issue.EvidenceId);
        Assert.Contains("entity", issue.Description);
    }

    [Fact]
    public void Validate_DuplicateEvidenceConflict_SurfacedFromMergeConflicts()
    {
        var conflict = new MergeConflict("evidence", "EV1", "conflicting_value", "evidence 'EV1' differs across sources");
        var amlCase = CaseWith(evidence: new[] { Evidence("EV1") }, conflicts: new[] { conflict });

        var result = EvidenceIntegrityValidator.Validate(amlCase);

        Assert.False(result.Passed);
        var issue = Assert.Single(result.DuplicateEvidenceIds);
        Assert.Equal("EV1", issue.EvidenceId);
    }

    [Fact]
    public void Validate_NonEvidenceMergeConflict_DoesNotAffectDuplicateEvidenceIds()
    {
        var conflict = new MergeConflict("transaction", "T1", "currency_mismatch", "T1 currency differs");
        var amlCase = CaseWith(transactions: new[] { Txn("T1") }, conflicts: new[] { conflict });

        var result = EvidenceIntegrityValidator.Validate(amlCase);

        Assert.Empty(result.DuplicateEvidenceIds);
        Assert.True(result.Passed); // no evidence-integrity failure just because a transaction conflict exists elsewhere
    }

    [Fact]
    public void Validate_MultipleDanglingReferencesInOneRelationship_AllReported()
    {
        var amlCase = CaseWith(relationships: new[] { Rel("R1", new[] { "MISSING1", "MISSING2" }) });

        var result = EvidenceIntegrityValidator.Validate(amlCase);

        Assert.Equal(2, result.DanglingReferences.Count);
    }

    [Fact]
    public void Validate_MultipleReferencesAcrossRelationshipsAndSars_AllCategoriesPopulated()
    {
        var amlCase = CaseWith(
            transactions: new[] { Txn("T1") },
            relationships: new[] { Rel("R1", new[] { "GHOST-EVIDENCE" }) },
            sars: new[] { Sar("SAR1", new[] { "GHOST-TXN" }) });

        var result = EvidenceIntegrityValidator.Validate(amlCase);

        Assert.False(result.Passed);
        Assert.Single(result.DanglingReferences);
        Assert.Single(result.MissingTransactionReferences);
    }

    [Fact]
    public void Validate_IsDeterministic_SameCaseValidatedTwiceProducesSameResult()
    {
        var amlCase = CaseWith(relationships: new[] { Rel("R1", new[] { "GHOST" }) });

        var r1 = EvidenceIntegrityValidator.Validate(amlCase);
        var r2 = EvidenceIntegrityValidator.Validate(amlCase);

        Assert.Equal(r1.Status, r2.Status);
        Assert.Equal(r1.DanglingReferences.Count, r2.DanglingReferences.Count);
        Assert.Equal(r1.DanglingReferences[0], r2.DanglingReferences[0]);
    }
}
