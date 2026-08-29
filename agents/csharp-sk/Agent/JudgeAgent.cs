using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AmlAgent.Adapters;
using AmlAgent.Adapters.Canonical;
using AmlAgent.Evidence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AmlAgent.Agent;

// <summary>
// LLM-as-judge subcommand. Loads a task rubric, the candidate's outputs from
// a benchmark workspace and the grounding data, then asks the Semantic Kernel
/// chat model to score the candidate against each rubric dimension and emit
/// structured JSON. The result is written to &lt;workspace&gt;/judge_report.json
/// and is also validated by xUnit in tests/AmlAgent.Tests/JudgeReportTests.cs.
///
/// Used by AML-Agent-Bench to score qualitative aspects of regulatory output:
/// evidence citation, temporal reasoning, anomaly detection, fact/assumption
/// separation, compliance tone, and absence of unsupported claims.
///
/// It also computes the PhD's evidence-traceability measures on top of the
/// rubric scores:
///   - Evidence traceability (citation precision/recall) -- the PhD's sole
///     primary metric: computed entirely deterministically by regex-matching
///     cited transaction IDs in the report against a curated gold-evidence
///     set (rubric's "gold_evidence_annotations"), no LLM involved.
///   - Evidence-Grounded Hallucination Rate (EGHR) -- retained as a legacy/
///     secondary metric (see docs/evidence-traceability-framework.md
///     #legacy-eghr-metric): the LLM extracts atomic claims and self-labels
///     each as supported / unsupported / contradicted against the grounding
///     data, but AmlAgent.Evidence.EvidenceScoring deterministically
///     overrides any claim citing a nonexistent transaction ID to
///     "unsupported" -- the judge cannot inflate its own grounding.
// </summary>
internal static class JudgeAgent
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? taskId = null;
        string? workspace = null;
        string? rubricPathOverride = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--task" when i + 1 < args.Length: taskId = args[++i]; break;
                case "--workspace" when i + 1 < args.Length: workspace = args[++i]; break;
                case "--rubric" when i + 1 < args.Length: rubricPathOverride = args[++i]; break;
                case "-h" or "--help":
                    Console.WriteLine("aml-agent judge --task <id> --workspace <path> [--rubric <path>]");
                    return 0;
            }
        }

        if (workspace is null)
        {
            Console.Error.WriteLine("--workspace is required");
            return 64;
        }
        if (!Directory.Exists(workspace))
        {
            Console.Error.WriteLine($"workspace not found: {workspace}");
            return 1;
        }

        var rubricPath = rubricPathOverride
            ?? (taskId is null ? null : FindRubric(taskId));
        if (rubricPath is null || !File.Exists(rubricPath))
        {
            Console.Error.WriteLine($"rubric not found (looked for tasks/{taskId}/rubric.json). Pass --rubric explicitly.");
            return 1;
        }

        var rubricJson = await File.ReadAllTextAsync(rubricPath);
        var rubric = JsonNode.Parse(rubricJson)
            ?? throw new InvalidDataException("rubric.json is not valid JSON");

        double passThreshold = (double?)rubric["pass_threshold_overall"] ?? 0.7;
        var dimensions = rubric["dimensions"]?.AsArray()
            ?? throw new InvalidDataException("rubric.json missing 'dimensions'");
        var evalInputs = rubric["evaluation_inputs"]?.AsArray()?.Select(n => n!.GetValue<string>()).ToList()
            ?? new List<string>();
        var groundingInputs = rubric["grounding_inputs"]?.AsArray()?.Select(n => n!.GetValue<string>()).ToList()
            ?? new List<string>();

        var evalBundle = BundleFiles(workspace, evalInputs, "evaluation");
        var groundingBundle = BundleFiles(workspace, groundingInputs, "grounding");

        // Deterministic grounding: the real transaction IDs and (optionally) a
        // curated gold-evidence subset, used to score EGHR and traceability
        // independently of what the LLM claims. When the workspace carries a
        // case-definition.json (multi-source tasks like task-007), the judge
        // reloads it to get the FULL evidence universe -- accounts,
        // relationships, watchlist entries, SARs, not just transactions --
        // instead of only the txn_ids the flat grounding_inputs files expose.
        // Absent for every task that predates this feature, so caseEvidence
        // is null and every line below falls back to the original behaviour.
        var caseEvidence = LoadCaseEvidenceIfPresent(workspace);
        HashSet<string> validTxnIds;
        IReadOnlyCollection<EvidenceReference>? validEvidenceRefs = null;
        if (caseEvidence is not null)
        {
            validEvidenceRefs = caseEvidence;
            validTxnIds = new HashSet<string>(caseEvidence.Select(e => e.EvidenceId), StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            validTxnIds = ParseValidTxnIds(workspace, groundingInputs);
        }

        var goldTxnIds = LoadGoldEvidence(rubricPath, rubric);
        IReadOnlyCollection<EvidenceReference>? goldEvidenceRefs = null;
        if (validEvidenceRefs is not null && goldTxnIds is not null)
        {
            var lookup = new Dictionary<string, EvidenceReference>(StringComparer.OrdinalIgnoreCase);
            foreach (var reference in validEvidenceRefs) lookup.TryAdd(reference.EvidenceId, reference);
            goldEvidenceRefs = goldTxnIds
                .Select(id => lookup.TryGetValue(id, out var reference) ? reference : new EvidenceReference(id, "unknown"))
                .ToList();
        }

        // Fix #7: material claims with pre-authored reference evidence (Required/
        // AcceptableAlternatives), when a task's evidence-annotations.json defines
        // them. Materiality and reference evidence are authored by the task, not
        // guessed by the LLM -- the judge's only job per claim is to identify which
        // evidence ids the candidate's report actually cites in support of it,
        // which ClaimLevelScoring then scores deterministically. Empty for every
        // task without a "material_claims" annotation (task-006 today), so
        // claim_support_coverage stays null there exactly as before this fix.
        var materialClaims = LoadMaterialClaims(rubricPath, rubric);

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not set");
        var model = Environment.GetEnvironmentVariable("BENCH_JUDGE_MODEL")
            ?? Environment.GetEnvironmentVariable("BENCH_MODEL")
            ?? "gpt-4o-mini";

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId: model, apiKey: apiKey);
        builder.Services.AddLogging(c => c.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var kernel = builder.Build();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
            Temperature = 0.0,
            ResponseFormat = "json_object",
        };

        var rubricSummary = BuildRubricSummary(dimensions);

        var system =
            "You are a strict but fair regulatory compliance judge for an AML benchmark. " +
            "Score the candidate's output against each rubric dimension on a 0..max scale. " +
            "You must also extract atomic factual claims from the candidate's report and label each one. " +
            "A claim is 'supported' only if the cited grounding data directly confirms it; 'contradicted' " +
            "(intrinsic hallucination) if the grounding data shows something different from what is claimed; " +
            "'unsupported' (extrinsic hallucination) if no grounding data confirms it, including any claim " +
            "that cites a transaction ID you cannot find in the grounding data. " +
            "You MUST respond with a single JSON object and nothing else, matching the requested schema. " +
            "Penalise unsupported claims, accusatory language, and invented evidence. Reward cautious, " +
            "evidence-citing, regulator-friendly writing. If a list of MATERIAL CLAIMS is provided, you " +
            "must also assess each one against the candidate's report and list which evidence ids the " +
            "report actually cites for it -- do not invent a citation the report never made.";

        var user = new StringBuilder();
        user.AppendLine("RUBRIC DIMENSIONS:");
        user.AppendLine(rubricSummary);
        user.AppendLine();
        user.AppendLine("CANDIDATE OUTPUTS TO EVALUATE:");
        user.AppendLine(evalBundle);
        user.AppendLine();
        user.AppendLine("GROUND TRUTH DATA (use this to verify factual claims and citations):");
        user.AppendLine(groundingBundle);
        user.AppendLine();

        if (materialClaims.Count > 0)
        {
            user.AppendLine("MATERIAL CLAIMS TO ASSESS: for EACH of the following pre-defined claims, decide whether the candidate's report asserts it (even if worded differently), and if so, list every evidence id (transaction id, relationship id, watchlist id, etc.) the report cites in the text supporting that specific assertion. If the report does not make this assertion at all, or makes it without citing any evidence, return an empty array for that claim -- do not guess or invent a citation.");
            foreach (var c in materialClaims)
                user.AppendLine($"- claim_id={c.ClaimId}: {c.Text}");
            user.AppendLine();
        }

        user.AppendLine("Return a JSON object with exactly this schema:");
        user.AppendLine("""
        {
          "scores": {
            "<dimension_id>": { "score": <int 0..max>, "max": <int>, "reasoning": "<one sentence>" },
            ...
          },
          "overall_score": <int sum of scores>,
          "overall_max": <int sum of maxes>,
          "overall_percentage": <float, overall_score / overall_max, 4 decimals>,
          "verdict": "<PASS or FAIL>",
          "claims": [
            {
              "text": "<one atomic factual claim from the candidate's report, quoted or closely paraphrased>",
              "cited_txn_ids": ["<transaction IDs this specific claim relies on, e.g. T2-014>"],
              "support": "<supported | unsupported | contradicted>"
            }
          ],
          "material_claim_assessments": [
            {
              "claim_id": "<claim_id from the MATERIAL CLAIMS list above -- one entry per claim listed, even if the array is empty>",
              "cited_evidence_ids": ["<every evidence id the report cites in support of this specific claim, or [] if not asserted/not cited>"]
            }
          ]
        }
        """);

        var history = new ChatHistory();
        history.AddSystemMessage(system);
        history.AddUserMessage(user.ToString());

        Console.WriteLine($"[judge] model={model} rubric={rubricPath}");
        var response = await chat.GetChatMessageContentWithRetryAsync(history, settings, kernel, "judge");
        var raw = response.Content ?? "";

        JsonNode parsed;
        try
        {
            parsed = JsonNode.Parse(raw) ?? throw new InvalidDataException("LLM returned empty JSON");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[judge] failed to parse LLM JSON: {ex.Message}");
            Console.Error.WriteLine("[judge] raw response:");
            Console.Error.WriteLine(raw);
            return 1;
        }

        // Recompute overall to defend against arithmetic errors in the LLM output.
        // Also recompute per-category subtotals (fix #5): rubric.json dimensions
        // may each carry an optional "category" -- outcome_correctness,
        // evidence_quality, or process_quality -- so the overall rubric score
        // (a holistic "is this report good enough" gate, unchanged in meaning)
        // stays separate from a construct-clean outcome-correctness score (no
        // citation-quality terms) that H4 can actually correlate against
        // evidence traceability without contaminating both sides of the
        // comparison. See docs/evidence-traceability-framework.md
        // #outcome-correctness-vs-task-performance.
        int overallScore = 0, overallMax = 0;
        var dimensionCategories = dimensions
            .Select(d => ((string)d!["id"]!, (string?)d["category"]))
            .ToDictionary(t => t.Item1, t => t.Item2);
        var scoredDimensions = new List<RubricCategoryScoring.ScoredDimension>();

        var scores = parsed["scores"]?.AsObject();
        if (scores is not null)
        {
            foreach (var (id, node) in scores)
            {
                int s = (int?)node?["score"] ?? 0;
                int m = (int?)node?["max"] ?? 0;
                overallScore += s;
                overallMax += m;
                scoredDimensions.Add(new RubricCategoryScoring.ScoredDimension(id, s, m));
            }
        }
        var categoryTotals = RubricCategoryScoring.ComputeCategoryTotals(scoredDimensions, dimensionCategories);
        double percentage = overallMax == 0 ? 0.0 : Math.Round((double)overallScore / overallMax, 4);
        string verdict = percentage >= passThreshold ? "PASS" : "FAIL";

        parsed["overall_score"] = overallScore;
        parsed["overall_max"] = overallMax;
        parsed["overall_percentage"] = percentage;
        parsed["verdict"] = verdict;
        parsed["pass_threshold_overall"] = passThreshold;
        parsed["task"] = taskId;
        parsed["model"] = model;
        parsed["judged_at_utc"] = DateTime.UtcNow.ToString("o");

        parsed["rubric_by_category"] = new JsonObject(categoryTotals.Select(kv => KeyValuePair.Create(
            kv.Key,
            (JsonNode?)new JsonObject
            {
                ["score"] = kv.Value.Score,
                ["max"] = kv.Value.Max,
                ["percentage"] = kv.Value.Percentage,
            })));
        // Convenience top-level alias for the specific field H4 needs: the
        // outcome-correctness-only score, entirely free of the citation-quality
        // dimensions (evidence_grounding, avoids_unsupported_claims,
        // evidence_traceability, ...) that make up "evidence_quality" above.
        // Null (not zero) when this rubric has no outcome_correctness-tagged
        // dimensions at all, so a caller can't mistake "not measured" for "0%".
        parsed["outcome_correctness"] = categoryTotals.TryGetValue("outcome_correctness", out var oc)
            ? new JsonObject { ["score"] = oc.Score, ["max"] = oc.Max, ["percentage"] = oc.Percentage }
            : null;

        // --- Legacy/secondary metric: Evidence-Grounded Hallucination Rate ---
        var claimInputs = ParseClaimInputs(parsed["claims"]);
        var eghr = EvidenceScoring.ScoreClaims(claimInputs, validTxnIds);
        parsed["claims"] = ClaimsToJson(eghr.Claims);
        parsed["eghr"] = new JsonObject
        {
            ["method"] = "llm_claim_extraction_with_deterministic_citation_override",
            ["claims_extracted"] = eghr.TotalClaims > 0,
            ["total_claims"] = eghr.TotalClaims,
            ["supported_count"] = eghr.SupportedCount,
            ["unsupported_count"] = eghr.UnsupportedCount,
            ["contradicted_count"] = eghr.ContradictedCount,
            ["rate"] = eghr.Rate,
        };

        // --- Primary metric: evidence traceability (citation precision/recall) ---
        var traceability = validEvidenceRefs is not null
            ? EvidenceScoring.ComputeTraceability(evalBundle, validEvidenceRefs, goldEvidenceRefs)
            : EvidenceScoring.ComputeTraceability(evalBundle, validTxnIds, goldTxnIds);
        parsed["evidence_traceability"] = new JsonObject
        {
            ["method"] = validEvidenceRefs is not null
                ? "deterministic_known_evidence_id_citation_match"
                : "deterministic_regex_citation_match",
            ["cited_txn_ids_total"] = traceability.CitedTotal,
            ["cited_txn_ids_distinct"] = traceability.CitedDistinct,
            ["fabricated_citations"] = ToJsonArray(traceability.FabricatedCitations),
            ["grounded_citations_distinct"] = traceability.GroundedDistinct,
            ["grounded_citations"] = ToJsonArray(traceability.GroundedCitations),
            ["gold_evidence_total"] = traceability.GoldTotal,
            ["gold_evidence_txn_ids"] = ToJsonArray(traceability.GoldEvidenceTxnIds),
            ["matched_gold_citations"] = traceability.MatchedGoldCitations,
            ["matched_gold_citations_list"] = ToJsonArray(traceability.MatchedGoldCitationsList),
            ["missing_gold_citations_list"] = ToJsonArray(traceability.MissingGoldCitationsList),
            ["precision"] = traceability.Precision,
            ["recall"] = traceability.Recall,
            ["f1"] = traceability.F1,
            // Fix #4: precision has two defensible denominators when a report
            // cites a fabricated id alongside real ones -- see
            // docs/evidence-traceability-framework.md#evidence-precision-ep.
            // "precision"/"f1" above are the primary, standard-IR-definition
            // metric (fabricated citations count against the denominator).
            // These two preserve the metric's original formula (real
            // citations only) under an explicit name, for anyone who wants
            // precision reported independently of fabrication.
            ["valid_evidence_precision"] = traceability.ValidEvidencePrecision,
            ["valid_evidence_f1"] = traceability.ValidEvidenceF1,
        };

        // --- Claim Support Coverage (fix #7) ---
        // Merges each material-claim template (claim_id/text/reference evidence,
        // authored in evidence-annotations.json) with the LLM's per-claim citation
        // assessment above into real AmlAgent.Evidence.Claim objects, so
        // ClaimLevelScoring can compute Claim Support Coverage deterministically
        // (superset-of-Required-or-one-AcceptableAlternatives-set) once it reaches
        // AssuranceProfileBuilder. A claim the LLM never returned an assessment for
        // (missing from material_claim_assessments) is treated as zero evidence
        // cited -- absence of an assessment must never be silently read as support.
        if (materialClaims.Count > 0)
        {
            var assessments = ParseMaterialClaimAssessments(parsed["material_claim_assessments"]);
            var claims = materialClaims.Select(c => new Claim(
                ClaimId: c.ClaimId,
                Text: c.Text,
                Material: true,
                AgentEvidence: assessments.GetValueOrDefault(c.ClaimId, new List<string>()),
                ReferenceEvidence: c.ReferenceEvidence)).ToList();

            parsed["material_claims"] = ClaimJson.ToJsonArray(claims);

            var csc = ClaimLevelScoring.ComputeClaimSupportCoverage(claims);
            Console.WriteLine(csc is double c
                ? $"[judge] claim support coverage: {c:P1} ({claims.Count(ClaimLevelScoring.IsSupported)}/{claims.Count} material claims adequately supported)"
                : "[judge] claim support coverage: no scorable material claims");
        }

        var outPath = Path.Combine(workspace, "judge_report.json");
        var finalJson = parsed.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outPath, finalJson);

        Console.WriteLine();
        Console.WriteLine($"[judge] wrote {outPath}");
        Console.WriteLine($"[judge] overall: {overallScore}/{overallMax} = {percentage:P1}");
        Console.WriteLine($"[judge] verdict: {verdict} (threshold {passThreshold:P0})");
        Console.WriteLine($"[judge] EGHR: {eghr.Rate:P1} ({eghr.UnsupportedCount} unsupported + {eghr.ContradictedCount} contradicted / {eghr.TotalClaims} claims)");
        if (traceability.Precision is double p && traceability.Recall is double r)
            Console.WriteLine($"[judge] evidence traceability: precision={p:P1} recall={r:P1} (matched {traceability.MatchedGoldCitations}/{traceability.GoldTotal} gold citations)");
        else
            Console.WriteLine("[judge] evidence traceability: no gold_evidence_annotations for this task, skipped");
        if (traceability.FabricatedCitations.Count > 0)
            Console.WriteLine($"[judge] WARNING: fabricated citations (not in source data): {string.Join(", ", traceability.FabricatedCitations)}");

        return verdict == "PASS" ? 0 : 1;
    }

    private static string? FindRubric(string taskId)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tasks", taskId, "rubric.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private static string BundleFiles(string workspace, List<string> relPaths, string label)
    {
        var sb = new StringBuilder();
        foreach (var rel in relPaths)
        {
            var full = Path.Combine(workspace, rel);
            sb.AppendLine($"----- {label}:{rel} -----");
            if (!File.Exists(full))
            {
                sb.AppendLine("[MISSING FILE]");
                continue;
            }
            sb.AppendLine(File.ReadAllText(full));
        }
        return sb.ToString();
    }

    private static string BuildRubricSummary(JsonArray dimensions)
    {
        var sb = new StringBuilder();
        foreach (var d in dimensions)
        {
            sb.AppendLine($"- id={d!["id"]}  max={d["max"]}  : {d["description"]}");
        }
        return sb.ToString();
    }

    /// <summary>Reads every grounding CSV in the workspace and unions their txn_id columns.</summary>
    /// <summary>
    /// Reads every grounding file's txn_ids, dispatching by extension via
    /// EvidenceScoring.ParseTxnIdsFromFile -- CSV and JSON grounding data
    /// are both supported (and unioned if a task provides both, e.g. two
    /// representations of the same ledger). An unsupported format
    /// contributes no IDs but doesn't throw, so a mixed-format
    /// grounding_inputs list degrades gracefully rather than crashing.
    /// </summary>
    private static HashSet<string> ParseValidTxnIds(string workspace, List<string> groundingInputs)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in groundingInputs)
        {
            var full = Path.Combine(workspace, rel);
            if (!File.Exists(full)) continue;
            foreach (var id in EvidenceScoring.ParseTxnIdsFromFile(File.ReadAllText(full), rel))
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// Loads the rubric's optional "gold_evidence_annotations" file (a task-definition
    /// file living next to rubric.json, not part of the staged workspace). Unions
    /// the legacy transaction-only field with the generalised "gold_evidence_ids"
    /// field (any evidence type -- relationship, watchlist, SAR, ...), so a task
    /// written before the generalised evidence model still works unchanged, and
    /// a multi-source task like task-007 can annotate gold evidence beyond txn_ids.
    /// </summary>
    private static HashSet<string>? LoadGoldEvidence(string rubricPath, JsonNode rubric)
    {
        var rel = rubric["gold_evidence_annotations"]?.GetValue<string>();
        if (string.IsNullOrEmpty(rel)) return null;

        var dir = Path.GetDirectoryName(Path.GetFullPath(rubricPath))!;
        var full = Path.Combine(dir, rel);
        if (!File.Exists(full)) return null;

        var doc = JsonNode.Parse(File.ReadAllText(full));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in doc?["gold_evidence_txn_ids"]?.AsArray() ?? new JsonArray())
            if (n is not null) ids.Add(n.GetValue<string>());
        foreach (var n in doc?["gold_evidence_ids"]?.AsArray() ?? new JsonArray())
            if (n is not null) ids.Add(n.GetValue<string>());

        return ids.Count > 0 ? ids : null;
    }

    /// <summary>One task-authored material claim template: id, text, and pre-authored reference evidence, before the LLM has assessed which evidence ids the candidate's report actually cites for it.</summary>
    private sealed record MaterialClaimTemplate(string ClaimId, string Text, ReferenceEvidence ReferenceEvidence);

    /// <summary>
    /// Loads the rubric's optional "gold_evidence_annotations" file's
    /// "material_claims" array (fix #7) -- pre-authored claim id/text/
    /// Required/AcceptableAlternatives, distinct from the flat gold-evidence
    /// id sets LoadGoldEvidence reads from the same file. Materiality is true
    /// by construction (a task only lists claims it considers material) and
    /// reference evidence is authored by the task, not the LLM -- the judge's
    /// job is only to identify which evidence ids the report cites for each
    /// one (see the MATERIAL CLAIMS section built into the judge prompt).
    /// Returns an empty list (never throws) for any task without this
    /// annotation, so claim_support_coverage stays null exactly as before
    /// this fix for every task that doesn't opt in.
    /// </summary>
    private static IReadOnlyList<MaterialClaimTemplate> LoadMaterialClaims(string rubricPath, JsonNode rubric)
    {
        var rel = rubric["gold_evidence_annotations"]?.GetValue<string>();
        if (string.IsNullOrEmpty(rel)) return Array.Empty<MaterialClaimTemplate>();

        var dir = Path.GetDirectoryName(Path.GetFullPath(rubricPath))!;
        var full = Path.Combine(dir, rel);
        if (!File.Exists(full)) return Array.Empty<MaterialClaimTemplate>();

        var doc = JsonNode.Parse(File.ReadAllText(full));
        var arr = doc?["material_claims"]?.AsArray();
        if (arr is null) return Array.Empty<MaterialClaimTemplate>();

        var templates = new List<MaterialClaimTemplate>();
        foreach (var node in arr)
        {
            if (node is not JsonObject obj) continue;
            var claimId = (string?)obj["claim_id"];
            var text = (string?)obj["text"];
            if (string.IsNullOrEmpty(claimId) || string.IsNullOrEmpty(text)) continue;

            var required = obj["required"]?.AsArray()?.Select(n => (string?)n ?? "").ToList() ?? new List<string>();
            var alternatives = obj["acceptable_alternatives"]?.AsArray()?
                .Select(alt => (IReadOnlyList<string>)(alt?.AsArray()?.Select(n => (string?)n ?? "").ToList() ?? new List<string>()))
                .ToList();

            templates.Add(new MaterialClaimTemplate(claimId, text, new ReferenceEvidence(required, alternatives)));
        }
        return templates;
    }

    /// <summary>
    /// Parses the LLM's "material_claim_assessments" response into a
    /// claim_id -> cited-evidence-ids lookup. A malformed or missing entry
    /// for a given claim is absent from the dictionary (GetValueOrDefault at
    /// the call site then treats it as zero evidence cited), never crashes
    /// the judge run over one bad LLM field.
    /// </summary>
    private static Dictionary<string, List<string>> ParseMaterialClaimAssessments(JsonNode? node)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var arr = node?.AsArray();
        if (arr is null) return result;

        foreach (var entry in arr)
        {
            if (entry is not JsonObject obj) continue;
            var claimId = (string?)obj["claim_id"];
            if (string.IsNullOrEmpty(claimId)) continue;

            var ids = obj["cited_evidence_ids"]?.AsArray()?.Select(n => (string?)n ?? "").ToList() ?? new List<string>();
            result[claimId] = ids;
        }
        return result;
    }

    /// <summary>
    /// Multi-source tasks stage a case-definition.json into the workspace
    /// root (see AmlAgent.Harness.Program.StageCanonicalCaseIfPresent), which
    /// the harness already used once to materialise workspace/data/*.csv|json
    /// for the agent to read. The judge reloads that same case-definition.json
    /// independently -- rather than re-parsing the exported flat files -- so it
    /// gets typed EvidenceReferences for every canonical record (transactions,
    /// accounts, relationships, watchlist entries, SARs, ...), not just the
    /// txn_id column a flat CSV exposes. Returns null (never throws) for any
    /// task without a case-definition.json, or one that fails to reload, so
    /// callers fall back to the original flat-grounding-inputs path exactly as
    /// before this feature existed.
    /// </summary>
    private static IReadOnlyList<EvidenceReference>? LoadCaseEvidenceIfPresent(string workspace)
    {
        var caseDefPath = Path.Combine(workspace, "case-definition.json");
        if (!File.Exists(caseDefPath)) return null;

        try
        {
            var definition = CaseDefinitionReader.Parse(File.ReadAllText(caseDefPath), caseDefPath, workspace);
            var result = CaseLoader.LoadAsync(definition, AdapterRegistry.CreateDefault()).GetAwaiter().GetResult();
            return result.MergedCase.ToEvidenceReferences();
        }
        catch (InvalidCaseDefinitionException ex)
        {
            Console.Error.WriteLine($"[judge]   invalid case-definition.json: {ex.Message} -- falling back to flat grounding_inputs");
            return null;
        }
    }

    private static List<ClaimInput> ParseClaimInputs(JsonNode? claimsNode)
    {
        var result = new List<ClaimInput>();
        var arr = claimsNode?.AsArray();
        if (arr is null) return result;

        foreach (var c in arr)
        {
            if (c is null) continue;
            var text = c["text"]?.GetValue<string>() ?? "";
            var cited = c["cited_txn_ids"]?.AsArray()?
                .Select(n => n!.GetValue<string>()).ToList() ?? new List<string>();
            var support = c["support"]?.GetValue<string>() ?? "unsupported";
            result.Add(new ClaimInput(text, cited, support));
        }
        return result;
    }

    private static JsonArray ToJsonArray(IReadOnlyList<string> values) =>
        new JsonArray(values.Select(s => (JsonNode)s).ToArray());

    private static JsonArray ClaimsToJson(IReadOnlyList<ClaimResult> claims)
    {
        var arr = new JsonArray();
        foreach (var c in claims)
        {
            arr.Add(new JsonObject
            {
                ["text"] = c.Text,
                ["cited_txn_ids"] = new JsonArray(c.CitedTxnIds.Select(s => (JsonNode)s).ToArray()),
                ["support"] = c.Support,
                ["fabricated_citation"] = c.FabricatedCitation,
            });
        }
        return arr;
    }
}
