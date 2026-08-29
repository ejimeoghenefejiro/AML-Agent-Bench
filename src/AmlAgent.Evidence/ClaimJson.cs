using System.Text.Json.Nodes;

namespace AmlAgent.Evidence;

/// <summary>
/// JSON (de)serialisation for AmlAgent.Evidence.Claim/ReferenceEvidence, so
/// judge_report.json can carry claim-level data (fix #7) and
/// AssuranceProfileBuilder can round-trip it back into the typed model
/// ClaimLevelScoring/EvidenceTraceabilityProfileBuilder need, without either
/// side hand-rolling JsonObject shape logic. Pure, dependency-free, like the
/// rest of AmlAgent.Evidence -- no I/O.
/// </summary>
public static class ClaimJson
{
    public static JsonArray ToJsonArray(IReadOnlyList<Claim> claims) =>
        new(claims.Select(c => (JsonNode)ToJsonObject(c)).ToArray());

    public static JsonObject ToJsonObject(Claim claim) => new()
    {
        ["claim_id"] = claim.ClaimId,
        ["text"] = claim.Text,
        ["material"] = claim.Material,
        ["agent_evidence"] = new JsonArray(claim.AgentEvidence.Select(id => (JsonNode)id).ToArray()),
        ["reference_evidence"] = claim.ReferenceEvidence is null ? null : ToJsonObject(claim.ReferenceEvidence),
    };

    private static JsonObject ToJsonObject(ReferenceEvidence reference) => new()
    {
        ["required"] = new JsonArray(reference.Required.Select(id => (JsonNode)id).ToArray()),
        ["acceptable_alternatives"] = reference.AcceptableAlternatives is null
            ? null
            : new JsonArray(reference.AcceptableAlternatives
                .Select(alt => (JsonNode)new JsonArray(alt.Select(id => (JsonNode)id).ToArray()))
                .ToArray()),
        ["corroborating"] = reference.Corroborating is null
            ? null
            : new JsonArray(reference.Corroborating.Select(id => (JsonNode)id).ToArray()),
    };

    /// <summary>Parses a "material_claims"-shaped JSON array (see ToJsonArray) back into Claim objects. Malformed/missing fields are treated leniently (empty lists, Material defaults true), never throwing -- a partially-written judge_report.json should degrade, not crash the assurance-profile build.</summary>
    public static IReadOnlyList<Claim> ParseArray(JsonArray? array)
    {
        if (array is null) return Array.Empty<Claim>();

        var claims = new List<Claim>();
        foreach (var node in array)
        {
            if (node is not JsonObject obj) continue;
            var claimId = (string?)obj["claim_id"] ?? "";
            var text = (string?)obj["text"] ?? "";
            var material = (bool?)obj["material"] ?? true;
            var agentEvidence = obj["agent_evidence"]?.AsArray()?.Select(n => (string?)n ?? "").ToList()
                ?? new List<string>();
            var reference = ParseReferenceEvidence(obj["reference_evidence"] as JsonObject);

            claims.Add(new Claim(claimId, text, material, agentEvidence, reference));
        }
        return claims;
    }

    private static ReferenceEvidence? ParseReferenceEvidence(JsonObject? obj)
    {
        if (obj is null) return null;

        var required = obj["required"]?.AsArray()?.Select(n => (string?)n ?? "").ToList() ?? new List<string>();
        var alternatives = obj["acceptable_alternatives"]?.AsArray()?
            .Select(alt => (IReadOnlyList<string>)(alt?.AsArray()?.Select(n => (string?)n ?? "").ToList() ?? new List<string>()))
            .ToList();
        var corroborating = obj["corroborating"]?.AsArray()?.Select(n => (string?)n ?? "").ToList();

        return new ReferenceEvidence(required, alternatives, corroborating);
    }
}
