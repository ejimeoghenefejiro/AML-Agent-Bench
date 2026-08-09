using AmlAgent.Adapters.Canonical;
using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// Item 11: starts from a known-valid canonical case and deliberately corrupts it
/// in the seven ways listed in the research-validation instructions, verifying
/// what the case-integrity/assurance pipeline actually catches.
///
/// This test class proves both POSITIVE and NEGATIVE results, and the negative
/// ones are the more important research finding: EvidenceIntegrityValidator only
/// detects REFERENTIAL corruption (a reference that no longer resolves, or
/// resolves to a duplicate/wrong-typed record) and CanonicalCaseMerger only
/// detects VALUE corruption when there is a second, disagreeing source to compare
/// against. A single, internally-consistent, uncorroborated source that has been
/// silently altered (wrong amount, wrong timestamp, wrong beneficiary, retargeted
/// relationship, wrong watchlist link) that still points at real ids is currently
/// INVISIBLE to every layer of this pipeline. Per the instructions, this is
/// surfaced explicitly as a genuine limitation, not silently accepted or hidden.
/// </summary>
public class EvidenceCorruptionSensitivityTests
{
    private static SourceLineage Lineage(string sourceType, string id) => new(sourceType, $"{sourceType}-file", null, id, sourceType, "1.0.0");

    private static CanonicalTransaction Txn(string id, string src = "ACC1", string dst = "ACC2", decimal amount = 1000m,
        DateTimeOffset? ts = null, string sourceType = "csv") => new(
        id, src, dst, amount, "USD", ts ?? new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
        "wire", "US", false, Lineage(sourceType, id));

    private static CanonicalEntity Entity(string id, string sourceType = "graphml") => new(id, "Account", id, Lineage(sourceType, id));

    private static CanonicalRelationship Rel(string id, string src, string dst, IReadOnlyList<string> evidenceIds, string sourceType = "graphml") => new(
        id, src, dst, "transferred_to", evidenceIds, Lineage(sourceType, id));

    /// <summary>The known-valid baseline: 3 accounts, 1 transaction, 1 relationship citing it, 1 watchlist flag on the destination.</summary>
    private static CanonicalAmlCase ValidBaseline() => CanonicalCaseMerger.Merge(new[]
    {
        CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1") } },
        CanonicalAmlDataset.Empty() with
        {
            Entities = new[] { Entity("ACC1"), Entity("ACC2"), Entity("WATCHLIST1") },
            Relationships = new[]
            {
                Rel("R1", "ACC1", "ACC2", new[] { "T1" }),
                Rel("R-WATCH", "WATCHLIST1", "ACC2", Array.Empty<string>()),
            },
        },
    });

    [Fact]
    public void Baseline_IsGenuinelyValid_BeforeAnyCorruption()
    {
        var integrity = EvidenceIntegrityValidator.Validate(ValidBaseline());
        Assert.True(integrity.Passed);
    }

    // -- Corruptions that ARE detected (referential integrity) --

    [Fact]
    public void DanglingTransactionEvidence_IsDetected_AndBlocksAssurance()
    {
        var baseline = ValidBaseline();
        var corrupted = baseline with
        {
            Relationships = new[] { baseline.Relationships[0] with { EvidenceIds = new[] { "T-GHOST" } }, baseline.Relationships[1] },
        };

        var integrity = EvidenceIntegrityValidator.Validate(corrupted);
        Assert.False(integrity.Passed);
        Assert.Single(integrity.DanglingReferences);

        var assessment = AssuranceEngine.EvaluateCaseIntegrity(true, integrity.DanglingReferences.Count, integrity.DuplicateEvidenceIds.Count);
        var decision = AssuranceEngine.Decide(Array.Empty<MetricResult>().ToList(), Array.Empty<string>()) with { Overall = "PASS", Reason = "all metrics passed" };
        var gated = AssuranceEngine.ApplyCaseIntegrityGate(decision, assessment);
        Assert.Equal("NOT_READY_FOR_DEPLOYMENT", gated.Overall);
    }

    [Fact]
    public void DeletedEvidenceRecord_IsDetected_AsADanglingReference()
    {
        // "Deleting" T1 from the transaction set is mechanically identical to a
        // dangling reference once the relationship still cites it -- the
        // relationship doesn't know the record used to exist.
        var baseline = ValidBaseline();
        var corrupted = baseline with { Transactions = Array.Empty<CanonicalTransaction>() };

        var integrity = EvidenceIntegrityValidator.Validate(corrupted);
        Assert.False(integrity.Passed);
        Assert.Single(integrity.DanglingReferences);
        Assert.Equal("T1", integrity.DanglingReferences[0].EvidenceId);
    }

    // -- Corruptions that are detected ONLY when a second, disagreeing source
    // exists to compare against (via CanonicalCaseMerger's conflict detection) --

    [Fact]
    public void ChangedAmount_CrossSource_IsDetectedAsMergeConflict()
    {
        var original = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", amount: 1000m, sourceType: "csv") } };
        var corrupted = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", amount: 999999m, sourceType: "json") } };

        var merged = CanonicalCaseMerger.Merge(new[] { original, corrupted });
        Assert.Single(merged.Conflicts);
        Assert.Equal("conflicting_value", merged.Conflicts[0].ConflictType);
    }

    [Fact]
    public void ChangedTimestamp_CrossSource_IsDetectedAsMergeConflict()
    {
        var original = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", ts: new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero), sourceType: "csv") } };
        var corrupted = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", ts: new DateTimeOffset(2026, 3, 5, 9, 0, 0, TimeSpan.Zero), sourceType: "json") } };

        var merged = CanonicalCaseMerger.Merge(new[] { original, corrupted });
        Assert.Single(merged.Conflicts);
        Assert.Equal("timestamp_mismatch", merged.Conflicts[0].ConflictType);
    }

    [Fact]
    public void ChangedBeneficiary_CrossSource_IsDetectedAsMergeConflict()
    {
        var original = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", dst: "ACC2", sourceType: "csv") } };
        var corrupted = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", dst: "ACC-ATTACKER", sourceType: "json") } };

        var merged = CanonicalCaseMerger.Merge(new[] { original, corrupted });
        Assert.Single(merged.Conflicts);
    }

    [Fact]
    public void CrossSourceTransactionConflicts_AreRecorded_ButDoNotCurrentlyBlockAssurance()
    {
        // MAJOR FINDING: a genuine, DETECTED, RECORDED transaction-value conflict
        // (visible in case_manifest.json's merge_conflicts) does NOT currently gate
        // the assurance decision at all -- AssuranceEngine.EvaluateCaseIntegrity only
        // reads dangling/missing/incompatible references and EVIDENCE-type duplicate
        // conflicts (RecordType == "evidence"), never TRANSACTION-type conflicts. A
        // case with a corrupted, disputed amount/timestamp/beneficiary can still be
        // reported "case_evidence_integrity: passed" and receive an assurance PASS,
        // even though the underlying data is visibly, recordedly in dispute.
        var original = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", amount: 1000m, sourceType: "csv") } };
        var corrupted = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", amount: 999999m, sourceType: "json") } };
        var merged = CanonicalCaseMerger.Merge(new[] { original, corrupted });

        Assert.Single(merged.Conflicts); // the corruption IS recorded...
        Assert.Equal("transaction", merged.Conflicts[0].RecordType);

        var integrity = EvidenceIntegrityValidator.Validate(merged);
        Assert.True(integrity.Passed); // ...but evidence-integrity, which the assurance gate reads, says "passed" regardless

        var assessment = AssuranceEngine.EvaluateCaseIntegrity(true, integrity.DanglingReferences.Count + integrity.MissingTransactionReferences.Count + integrity.IncompatibleEvidenceTypes.Count, integrity.DuplicateEvidenceIds.Count);
        Assert.Empty(assessment.Reasons); // the assurance gate has nothing to act on here -- a real gap
    }

    // -- Corruptions that are currently INVISIBLE entirely (no second source, no
    // broken reference) -- the most severe finding of this test class --

    [Fact]
    public void SingleSourceAmountCorruption_WithNoSecondSourceToCompareAgainst_IsCompletelyInvisible()
    {
        // No merge conflict is possible (only one source contributes T1), and no
        // reference is broken (nothing points at T1 in this minimal fixture) --
        // a silently corrupted amount from a single, uncorroborated source passes
        // through the entire pipeline with zero detection anywhere.
        var corrupted = CanonicalAmlDataset.Empty() with { Transactions = new[] { Txn("T1", amount: 999999999m) } };
        var merged = CanonicalCaseMerger.Merge(new[] { corrupted });

        Assert.Empty(merged.Conflicts);
        Assert.True(EvidenceIntegrityValidator.Validate(merged).Passed);
        // No layer in this pipeline can distinguish this from a genuine transaction.
    }

    [Fact]
    public void ChangedRelationship_RetargetedToADifferentRealEntity_IsInvisibleToReferentialValidation()
    {
        // The relationship now points at ACC3 instead of ACC2 -- ACC3 genuinely
        // exists in the case, so nothing is "dangling". This is a content-accuracy
        // problem (is this the RIGHT relationship?), not a referential-integrity
        // problem, and EvidenceIntegrityValidator only checks the latter.
        var baseline = ValidBaseline();
        var retargeted = baseline with
        {
            Entities = baseline.Entities.Append(Entity("ACC3")).ToList(),
            Relationships = new[] { baseline.Relationships[0] with { TargetEntityId = "ACC3" }, baseline.Relationships[1] },
        };

        var integrity = EvidenceIntegrityValidator.Validate(retargeted);
        Assert.True(integrity.Passed); // no dangling/incompatible reference -- the retargeting is invisible
    }

    [Fact]
    public void IncorrectWatchlistLink_PointingAtADifferentRealAccount_IsInvisibleToReferentialValidation()
    {
        // The watchlist flag now corroborates the WRONG account (ACC1, an
        // uninvolved account, instead of ACC2). ACC1 genuinely exists, so this is
        // not a dangling reference either -- same blind spot as the relationship
        // retargeting case above, applied to watchlist corroboration specifically.
        var baseline = ValidBaseline();
        var misattributed = baseline with
        {
            Relationships = new[] { baseline.Relationships[0], baseline.Relationships[1] with { TargetEntityId = "ACC1" } },
        };

        var integrity = EvidenceIntegrityValidator.Validate(misattributed);
        Assert.True(integrity.Passed);
    }
}
