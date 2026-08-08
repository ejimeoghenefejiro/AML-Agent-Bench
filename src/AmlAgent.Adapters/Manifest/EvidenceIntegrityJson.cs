using System.Text.Json.Nodes;
using AmlAgent.Adapters.Canonical;

namespace AmlAgent.Adapters.Manifest;

/// <summary>Renders an EvidenceIntegrityResult as JSON for the case manifest / CLI output.</summary>
public static class EvidenceIntegrityJson
{
    public static JsonObject ToJson(EvidenceIntegrityResult result) => new()
    {
        ["status"] = result.Status,
        ["dangling_references"] = new JsonArray(result.DanglingReferences.Select(i => (JsonNode)IssueToJson(i)).ToArray()),
        ["missing_transaction_references"] = new JsonArray(result.MissingTransactionReferences.Select(i => (JsonNode)IssueToJson(i)).ToArray()),
        ["duplicate_evidence_ids"] = new JsonArray(result.DuplicateEvidenceIds.Select(i => (JsonNode)IssueToJson(i)).ToArray()),
        ["incompatible_evidence_types"] = new JsonArray(result.IncompatibleEvidenceTypes.Select(i => (JsonNode)IssueToJson(i)).ToArray()),
    };

    private static JsonObject IssueToJson(EvidenceIntegrityIssue issue) => new()
    {
        ["referencing_record_type"] = issue.ReferencingRecordType,
        ["referencing_record_id"] = issue.ReferencingRecordId,
        ["evidence_id"] = issue.EvidenceId,
        ["source_type"] = issue.ReferencingSourceLineage?.SourceType,
        ["source_name"] = issue.ReferencingSourceLineage?.SourceName,
        ["description"] = issue.Description,
    };
}
