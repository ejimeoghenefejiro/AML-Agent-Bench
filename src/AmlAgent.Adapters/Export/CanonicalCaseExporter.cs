using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AmlAgent.Adapters.Canonical;

namespace AmlAgent.Adapters.Export;

/// <summary>
/// Materialises a merged CanonicalAmlCase back into plain files a task's
/// environment already knows how to work with -- flat CSV/JSON under a data
/// directory -- so a benchmark task, the reference agent, and the judge's
/// evidence-grounding logic never need to know whether the underlying case
/// came from one CSV or from Parquet + SQL Server + Neo4j + REST. This is
/// the seam that keeps a task independent of source format, adapter type,
/// database vendor, and file layout: by the time anything downstream looks
/// at the workspace, it's just files, like every task before this one.
///
/// transactions.csv deliberately uses the "txn_id" column name (an accepted
/// alias in TransactionRowMapper, and the *default* id column
/// AmlAgent.Evidence.EvidenceScoring/JudgeAgent's grounding-input parser
/// already expects) so a task can list "data/transactions.csv" in
/// rubric.json's grounding_inputs with zero changes to the judge.
/// </summary>
public static class CanonicalCaseExporter
{
    public static void ExportToDirectory(CanonicalAmlCase amlCase, string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);

        if (amlCase.Transactions.Count > 0)
            File.WriteAllText(Path.Combine(dataDirectory, "transactions.csv"), TransactionsCsv(amlCase.Transactions));
        if (amlCase.Accounts.Count > 0)
            WriteJson(dataDirectory, "accounts.json", amlCase.Accounts.Select(a => new JsonObject
            {
                ["account_id"] = a.AccountId, ["owner"] = a.Owner, ["institution"] = a.Institution, ["currency"] = a.Currency,
            }));
        if (amlCase.Customers.Count > 0)
            WriteJson(dataDirectory, "customers.json", amlCase.Customers.Select(c => new JsonObject
            {
                ["customer_id"] = c.CustomerId, ["name"] = c.Name, ["risk_rating"] = c.RiskRating, ["jurisdiction"] = c.Jurisdiction,
            }));
        if (amlCase.Entities.Count > 0 || amlCase.Relationships.Count > 0)
            File.WriteAllText(Path.Combine(dataDirectory, "relationships.json"), RelationshipsJson(amlCase.Entities, amlCase.Relationships));
        if (amlCase.Cases.Count > 0)
            WriteJson(dataDirectory, "cases.json", amlCase.Cases.Select(c => new JsonObject
            {
                ["case_id"] = c.CaseId, ["title"] = c.Title, ["status"] = c.Status,
            }));
        if (amlCase.Alerts.Count > 0)
            WriteJson(dataDirectory, "alerts.json", amlCase.Alerts.Select(a => new JsonObject
            {
                ["alert_id"] = a.AlertId, ["case_id"] = a.CaseId, ["typology"] = a.Typology, ["severity"] = a.Severity,
            }));
        if (amlCase.Evidence.Count > 0)
            WriteJson(dataDirectory, "evidence.json", amlCase.Evidence.Select(e => new JsonObject
            {
                ["evidence_id"] = e.EvidenceId, ["evidence_type"] = e.EvidenceType, ["description"] = e.Description,
                ["related_record_ids"] = new JsonArray(e.RelatedRecordIds.Select(id => (JsonNode)id).ToArray()),
            }));
        if (amlCase.Jurisdictions.Count > 0)
            WriteJson(dataDirectory, "jurisdictions.json", amlCase.Jurisdictions.Select(j => new JsonObject
            {
                ["code"] = j.Code, ["name"] = j.Name, ["high_risk"] = j.HighRisk,
            }));
        if (amlCase.Sars.Count > 0)
            WriteJson(dataDirectory, "sars.json", amlCase.Sars.Select(s => new JsonObject
            {
                ["sar_id"] = s.SarId, ["case_id"] = s.CaseId,
                ["transaction_ids"] = new JsonArray(s.TransactionIds.Select(id => (JsonNode)id).ToArray()),
                ["narrative"] = s.Narrative,
            }));
    }

    private static string TransactionsCsv(IReadOnlyList<CanonicalTransaction> transactions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("txn_id,source_account,destination_account,amount,currency,timestamp,channel,jurisdiction,sar_linked");
        foreach (var t in transactions.OrderBy(t => t.TransactionId, StringComparer.Ordinal))
        {
            sb.Append(t.TransactionId).Append(',')
              .Append(t.SourceAccount).Append(',')
              .Append(t.DestinationAccount).Append(',')
              .Append(t.Amount.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(t.Currency).Append(',')
              .Append(t.Timestamp.ToString("o", CultureInfo.InvariantCulture)).Append(',')
              .Append(t.Channel).Append(',')
              .Append(t.Jurisdiction).Append(',')
              .Append(t.SarLinked ? "true" : "false")
              .Append('\n');
        }
        return sb.ToString();
    }

    private static string RelationshipsJson(IReadOnlyList<CanonicalEntity> entities, IReadOnlyList<CanonicalRelationship> relationships)
    {
        var root = new JsonObject
        {
            ["entities"] = new JsonArray(entities.Select(e => (JsonNode)new JsonObject
            {
                ["entity_id"] = e.EntityId, ["entity_type"] = e.EntityType, ["display_name"] = e.DisplayName,
            }).ToArray()),
            ["relationships"] = new JsonArray(relationships.Select(r => (JsonNode)new JsonObject
            {
                ["relationship_id"] = r.RelationshipId, ["source_entity_id"] = r.SourceEntityId,
                ["target_entity_id"] = r.TargetEntityId, ["relationship_type"] = r.RelationshipType,
                ["evidence_ids"] = new JsonArray(r.EvidenceIds.Select(id => (JsonNode)id).ToArray()),
            }).ToArray()),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void WriteJson(string dataDirectory, string fileName, IEnumerable<JsonObject> records)
    {
        var array = new JsonArray(records.Select(r => (JsonNode)r).ToArray());
        File.WriteAllText(Path.Combine(dataDirectory, fileName), array.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
