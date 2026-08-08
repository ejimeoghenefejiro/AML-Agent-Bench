using AmlAgent.Adapters;
using AmlAgent.Adapters.Manifest;

namespace AmlAgent.Harness;

/// <summary>
/// `aml-harness load-case --case &lt;case-definition.json&gt; [--out &lt;case_manifest.json&gt;]`
///
/// A deliberate, dedicated multi-source counterpart to load-dataset: reads a
/// case-definition file naming several sources (each with a role, e.g.
/// "transactions"/"customers"/"relationships"/"watchlist"), resolves and
/// loads each one, merges them via CanonicalCaseMerger, validates
/// cross-source evidence references via EvidenceIntegrityValidator, and
/// writes case_manifest.json. Kept as its own subcommand rather than a
/// --source repeated 4 times on load-dataset, because a case is a distinct
/// concept from a single dataset load (it has merge conflicts and
/// evidence-integrity results that a single source never does).
/// </summary>
internal static class LoadCaseCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        string? caseDefinitionPath = null;
        string outPath = "case_manifest.json";

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--case" when i + 1 < args.Length: caseDefinitionPath = args[++i]; break;
                case "--out"  when i + 1 < args.Length: outPath = args[++i]; break;
                default:
                    Console.Error.WriteLine($"load-case: unknown argument: {args[i]}");
                    PrintUsage();
                    return 64;
            }
        }

        if (string.IsNullOrWhiteSpace(caseDefinitionPath))
        {
            Console.Error.WriteLine("load-case: --case is required");
            return 64;
        }

        if (!File.Exists(caseDefinitionPath))
        {
            Console.Error.WriteLine($"load-case: file not found: {caseDefinitionPath}");
            return 64;
        }

        CaseDefinition definition;
        try
        {
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(caseDefinitionPath));
            definition = CaseDefinitionReader.Parse(File.ReadAllText(caseDefinitionPath), caseDefinitionPath, baseDir);
        }
        catch (InvalidCaseDefinitionException ex)
        {
            Console.Error.WriteLine($"load-case: {ex.Message}");
            return 64;
        }

        Console.WriteLine($"[load-case] case_id = {definition.CaseId}");
        Console.WriteLine($"[load-case] sources = {definition.Sources.Count}");

        var registry = AdapterRegistry.CreateDefault();
        var result = CaseLoader.LoadAsync(definition, registry).GetAwaiter().GetResult();
        var generatedAtUtc = DateTimeOffset.UtcNow;

        foreach (var manifestEntry in result.MergedCase.SourceManifest)
            Console.WriteLine($"[load-case] loaded  {manifestEntry.SourceType,-12} adapter={manifestEntry.Adapter} v{manifestEntry.AdapterVersion} records={manifestEntry.RecordCount}");
        foreach (var failure in result.Failures)
            Console.Error.WriteLine($"[load-case] FAILED  {failure.SourceType,-12} role={failure.Role ?? "?"}: {failure.ErrorMessage}");

        if (result.MergedCase.Conflicts.Count > 0)
        {
            Program.PrintTable("Merge conflicts", new[] { "Record Type", "Record Id", "Conflict Type", "Description" },
                result.MergedCase.Conflicts.Select(c => new[] { c.RecordType, c.RecordId, c.ConflictType, c.Description }).ToList());
        }
        else
        {
            Console.WriteLine("[load-case] no merge conflicts");
        }

        Console.WriteLine();
        Console.WriteLine($"[load-case] evidence_integrity = {result.EvidenceIntegrity.Status}");
        PrintIntegrityIssues("dangling_references", result.EvidenceIntegrity.DanglingReferences);
        PrintIntegrityIssues("missing_transaction_references", result.EvidenceIntegrity.MissingTransactionReferences);
        PrintIntegrityIssues("duplicate_evidence_ids", result.EvidenceIntegrity.DuplicateEvidenceIds);
        PrintIntegrityIssues("incompatible_evidence_types", result.EvidenceIntegrity.IncompatibleEvidenceTypes);

        var manifest = CaseManifestBuilder.Build(result, generatedAtUtc);
        CaseManifestBuilder.Write(manifest, outPath);
        Console.WriteLine();
        Console.WriteLine($"[load-case] canonical_case_hash = {(string?)manifest["canonical_case_hash"]}");
        Console.WriteLine($"[load-case] wrote {Path.GetFullPath(outPath)}");

        if (!result.AllSourcesLoaded) return 66;
        if (!result.EvidenceIntegrity.Passed) return 67;
        return 0;
    }

    private static void PrintIntegrityIssues(string label, IReadOnlyList<Adapters.Canonical.EvidenceIntegrityIssue> issues)
    {
        if (issues.Count == 0) return;
        Console.WriteLine($"[load-case]   {label}:");
        foreach (var issue in issues)
            Console.WriteLine($"[load-case]     - {issue.Description}");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("aml-harness load-case --case <case-definition.json> [options]");
        Console.WriteLine();
        Console.WriteLine("Loads a multi-source case: resolves each source's adapter, loads it, merges every");
        Console.WriteLine("successfully-loaded source into one CanonicalAmlCase, validates cross-source evidence");
        Console.WriteLine("references, and writes case_manifest.json.");
        Console.WriteLine();
        Console.WriteLine("  --case <path>   case-definition JSON: {\"case_id\": \"...\", \"sources\": [{\"source_type\": \"csv\", \"role\": \"transactions\", \"path\": \"...\"}, ...]}");
        Console.WriteLine("  --out <path>    output path (default: case_manifest.json)");
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0 = loaded and evidence-integrity-clean, 64 = usage/definition error,");
        Console.WriteLine("  66 = one or more sources failed to load, 67 = evidence integrity validation failed");
        Console.WriteLine("  (case_manifest.json is still written in both failure cases -- failures are never silently dropped).");
    }
}
