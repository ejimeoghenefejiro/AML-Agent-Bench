using System.Text.Json;
using AmlAgent.Adapters;
using AmlAgent.Adapters.Manifest;

namespace AmlAgent.Harness;

/// <summary>
/// `aml-harness load-dataset --source-type &lt;type&gt; [--path &lt;file&gt;] [--connection-profile &lt;name&gt;]
///   [--query &lt;q&gt;] [--option key=value ...] [--out &lt;manifest.json&gt;]`
///
/// A standalone CLI entry point onto the multi-format Data Adapter Layer
/// (AmlAgent.Adapters): resolves the requested source type via
/// AdapterRegistry, loads it into the canonical model, and writes
/// dataset_manifest.json (dataset_id, adapter identity, schema version,
/// record counts, dataset_hash, normalisation_hash). Entirely separate from
/// the `--task`/`--local` benchmark-run flow and from compare/regress --
/// adding this subcommand does not touch bench_result.json,
/// assurance_profile.json, or any existing exit code.
/// </summary>
internal static class LoadDatasetCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return 0;
        }

        string? sourceType = null;
        string? path = null;
        string? connectionProfile = null;
        string? query = null;
        string outPath = "dataset_manifest.json";
        var extraOptions = new Dictionary<string, string>();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source-type"        when i + 1 < args.Length: sourceType = args[++i]; break;
                case "--path"               when i + 1 < args.Length: path = args[++i]; break;
                case "--connection-profile" when i + 1 < args.Length: connectionProfile = args[++i]; break;
                case "--query"              when i + 1 < args.Length: query = args[++i]; break;
                case "--out"                when i + 1 < args.Length: outPath = args[++i]; break;
                case "--option"             when i + 1 < args.Length:
                    var kv = args[++i].Split('=', 2);
                    if (kv.Length != 2)
                    {
                        Console.Error.WriteLine($"load-dataset: --option must be key=value, got '{args[i]}'");
                        return 64;
                    }
                    extraOptions[kv[0]] = kv[1];
                    break;
                default:
                    Console.Error.WriteLine($"load-dataset: unknown argument: {args[i]}");
                    PrintUsage();
                    return 64;
            }
        }

        if (string.IsNullOrWhiteSpace(sourceType))
        {
            Console.Error.WriteLine("load-dataset: --source-type is required");
            return 64;
        }

        var registry = AdapterRegistry.CreateDefault();
        var source = new DataSourceConfiguration(sourceType, path, connectionProfile, query,
            extraOptions.Count > 0 ? extraOptions : null);

        IAmlDataAdapter adapter;
        try
        {
            adapter = registry.Resolve(sourceType);
        }
        catch (UnsupportedSourceTypeException ex)
        {
            Console.Error.WriteLine($"load-dataset: {ex.Message}");
            return 65;
        }

        Adapters.Canonical.CanonicalAmlDataset dataset;
        var snapshotTimestamp = DateTimeOffset.UtcNow;
        try
        {
            dataset = adapter.LoadAsync(source).GetAwaiter().GetResult();
        }
        catch (AdapterException ex)
        {
            Console.Error.WriteLine($"load-dataset: {ex.Message}");
            return 65;
        }

        var manifest = DatasetManifestBuilder.BuildAsync(source, adapter, dataset, snapshotTimestamp).GetAwaiter().GetResult();
        DatasetManifestBuilder.Write(manifest, outPath);

        Console.WriteLine($"[load-dataset] adapter = {adapter.AdapterId} v{adapter.AdapterVersion}");
        Console.WriteLine($"[load-dataset] schema  = {dataset.SchemaVersion}");
        Console.WriteLine($"[load-dataset] records = {dataset.TotalRecordCount} " +
            $"(transactions={dataset.Transactions.Count}, entities={dataset.Entities.Count}, relationships={dataset.Relationships.Count})");
        Console.WriteLine($"[load-dataset] wrote {Path.GetFullPath(outPath)}");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("aml-harness load-dataset --source-type <type> [options]");
        Console.WriteLine();
        Console.WriteLine("Loads a dataset through the multi-format Data Adapter Layer and writes dataset_manifest.json.");
        Console.WriteLine();
        Console.WriteLine("  --source-type <type>          csv | json | jsonl | parquet | postgresql | sqlserver | rest | neo4j | graphml");
        Console.WriteLine("  --path <file>                 file-based sources (csv/json/jsonl/parquet/graphml)");
        Console.WriteLine("  --connection-profile <name>   database/API sources; resolved via AML_CONN_<NAME> env var, never a CLI value");
        Console.WriteLine("  --query <q>                   optional SQL/Cypher query or REST path override");
        Console.WriteLine("  --option key=value             adapter-specific extra option (repeatable), e.g. --option AuthTokenProfile=my-token");
        Console.WriteLine("  --out <manifest.json>          output path (default: dataset_manifest.json)");
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0 = loaded successfully, 64 = usage error, 65 = unsupported source type / invalid configuration / source or normalisation error.");
    }
}
