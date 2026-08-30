using System.Text.Json.Nodes;

namespace AmlAgent.Evidence;

/// <summary>
/// Schema + strict reader for an agent's OPTIONAL structured claim-evidence
/// output, `claim_evidence.json` (v0.3 validation-priorities item 4: the
/// structured-citation-output experimental condition). This is the agent's
/// own declaration of which evidence ids it believes support which claim --
/// the same information a free-text report carries implicitly in prose, just
/// typed and explicit instead of reverse-parsed.
///
/// Why this matters for Claim Support Coverage specifically: CSC (fix #7) is
/// "deterministic given the mapper's output", where the mapper is the LLM
/// judge identifying which evidence ids a free-text report cites for each
/// material claim (docs/evidence-traceability-framework.md
/// #claim-support-coverage-csc). When an agent instead produces this file,
/// JudgeAgent.cs uses the agent's own structured claims directly --
/// AgentEvidence comes from what the agent itself declared, not from an LLM
/// re-deriving it from prose. That removes the one non-deterministic step
/// CSC still had: under this condition, claim-level scoring is deterministic
/// end-to-end, not just "given the mapper's output".
///
/// The claim_id values an agent uses here are the SAME task-defined slot
/// labels its prompt already asks it to cover in prose (e.g. task-007's
/// prompt.md report outline already asks for victim/mule/aggregator/
/// watchlist/exit/clearance sections) -- this file does not give the agent
/// any information about REQUIRED evidence or correct answers it didn't
/// already have; it only asks the agent to express the same investigative
/// content in a structured, typed shape instead of prose. See
/// tasks/task-007-multi-source-mule-network/prompt.md's "Structured
/// citation output (optional)" section for exactly what an agent is told.
/// </summary>
public sealed record StructuredClaimEvidenceSet(string SchemaVersion, IReadOnlyList<StructuredClaim> Claims);

/// <summary>One of the agent's own claims: its own text, and the evidence ids (of any type -- transaction, relationship, watchlist, ...) it declares support it.</summary>
public sealed record StructuredClaim(string ClaimId, string Text, IReadOnlyList<StructuredEvidenceCitation> Evidence);

/// <summary>One evidence citation within a structured claim. EvidenceType is informational (what kind of record this is meant to be) -- matching is always by EvidenceId against the case's actual evidence universe, never by type.</summary>
public sealed record StructuredEvidenceCitation(string EvidenceId, string? EvidenceType);

/// <summary>
/// Parses an agent's claim_evidence.json. No claim or evidence data is ever
/// fabricated by this reader or by anything that calls it -- it only ever
/// returns what was actually present in the file, and throws a clear,
/// specific error for anything malformed rather than guessing or defaulting.
/// Deliberately lenient about EMPTY evidence lists (an agent that lists a
/// claim but cites no evidence for it is a real, meaningful signal --
/// unsupported -- not a parse error).
/// </summary>
public static class StructuredClaimEvidenceReader
{
    public const string CurrentSchemaVersion = "1.0";

    public static StructuredClaimEvidenceSet Parse(string json, string? sourcePathForErrors = null)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (Exception ex) { throw new InvalidStructuredClaimEvidenceException($"invalid JSON{Suffix(sourcePathForErrors)}: {ex.Message}"); }

        var obj = root as JsonObject
            ?? throw new InvalidStructuredClaimEvidenceException($"claim_evidence.json{Suffix(sourcePathForErrors)} must be a JSON object");

        var schemaVersion = (string?)obj["schema_version"];
        if (string.IsNullOrWhiteSpace(schemaVersion))
            throw new InvalidStructuredClaimEvidenceException($"claim_evidence.json{Suffix(sourcePathForErrors)} is missing required field 'schema_version'");

        var claimsNode = obj["claims"] as JsonArray;
        if (claimsNode is null)
            throw new InvalidStructuredClaimEvidenceException($"claim_evidence.json{Suffix(sourcePathForErrors)} is missing required field 'claims'");

        var claims = new List<StructuredClaim>();
        for (int i = 0; i < claimsNode.Count; i++)
        {
            if (claimsNode[i] is not JsonObject claimObj)
                throw new InvalidStructuredClaimEvidenceException($"claims[{i}]{Suffix(sourcePathForErrors)} must be a JSON object");

            var claimId = (string?)claimObj["claim_id"];
            if (string.IsNullOrWhiteSpace(claimId))
                throw new InvalidStructuredClaimEvidenceException($"claims[{i}]{Suffix(sourcePathForErrors)} is missing required field 'claim_id'");

            var text = (string?)claimObj["text"] ?? "";

            var evidenceNode = claimObj["evidence"] as JsonArray ?? new JsonArray();
            var evidence = new List<StructuredEvidenceCitation>();
            for (int j = 0; j < evidenceNode.Count; j++)
            {
                if (evidenceNode[j] is not JsonObject evObj)
                    throw new InvalidStructuredClaimEvidenceException($"claims[{i}] ('{claimId}').evidence[{j}]{Suffix(sourcePathForErrors)} must be a JSON object");

                var evidenceId = (string?)evObj["evidence_id"];
                if (string.IsNullOrWhiteSpace(evidenceId))
                    throw new InvalidStructuredClaimEvidenceException($"claims[{i}] ('{claimId}').evidence[{j}]{Suffix(sourcePathForErrors)} is missing required field 'evidence_id'");

                var evidenceType = (string?)evObj["evidence_type"];
                evidence.Add(new StructuredEvidenceCitation(evidenceId, evidenceType));
            }

            claims.Add(new StructuredClaim(claimId, text, evidence));
        }

        return new StructuredClaimEvidenceSet(schemaVersion, claims);
    }

    private static string Suffix(string? sourcePath) => sourcePath is null ? "" : $" ({sourcePath})";
}

public sealed class InvalidStructuredClaimEvidenceException : Exception
{
    public InvalidStructuredClaimEvidenceException(string message) : base(message) { }
}
