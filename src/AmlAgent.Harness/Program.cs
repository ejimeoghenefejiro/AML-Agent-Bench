using System.Diagnostics;
using AmlAgent.Oracle;

namespace AmlAgent.Harness;

/// <summary>
/// Language-agnostic Docker-based benchmark runner. Builds an agent image,
/// stages a temp workspace with the task's data + instruction.md, runs the
/// agent container, then runs the xUnit test project on the host against the
/// workspace via AML_BENCH_WORKSPACE.
///
/// Although the primary agent is C# + Semantic Kernel, the harness only
/// requires that an agent image read instruction.md from /app and exit 0 —
/// any language's agent can be benchmarked by dropping a Dockerfile under
/// agents/&lt;name&gt;/.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        string agent = "csharp-sk";
        string task = "aml-transaction-network";
        string? model = null;
        int maxSteps = 25;
        bool keepWorkspace = false;
        bool useOracle = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--agent" when i + 1 < args.Length: agent = args[++i]; break;
                case "--task"  when i + 1 < args.Length: task = args[++i]; break;
                case "--model" when i + 1 < args.Length: model = args[++i]; break;
                case "--max-steps" when i + 1 < args.Length: maxSteps = int.Parse(args[++i]); break;
                case "--keep-workspace": keepWorkspace = true; break;
                case "--oracle": useOracle = true; break;
                case "-h" or "--help": PrintUsage(); return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    PrintUsage();
                    return 64;
            }
        }

        var repoRoot = FindRepoRoot()
            ?? throw new InvalidOperationException("Could not locate repo root (looking for AML-Agent-Bench.sln)");

        var agentDir = Path.Combine(repoRoot, "agents", agent);
        var taskDir  = Path.Combine(repoRoot, "tasks", task);
        if (!Directory.Exists(agentDir)) { Console.Error.WriteLine($"agent not found: {agentDir}"); return 1; }
        if (!Directory.Exists(taskDir))  { Console.Error.WriteLine($"task not found: {taskDir}");  return 1; }

        var workspace = Path.Combine(Path.GetTempPath(), $"aml-bench-{task}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            StageWorkspace(taskDir, workspace);
            Console.WriteLine($"[harness] workspace: {workspace}");

            int agentRc;
            if (useOracle)
            {
                Console.WriteLine("[harness] --oracle: producing output via AmlAgent.Oracle (skipping agent container)");
                var input  = Path.Combine(workspace, "data", "transfers.csv");
                var output = Path.Combine(workspace, "aml_clusters.csv");
                var res = OracleRunner.Run(input, output);
                Console.WriteLine($"[harness] oracle wrote {res.ClustersWritten} clusters");
                agentRc = 0;
            }
            else
            {
                var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                    ?? throw new InvalidOperationException("OPENAI_API_KEY not set");

                var agentImage = BuildAgentImage(agent, agentDir);
                agentRc = RunAgentContainer(agentImage, workspace, apiKey, model, maxSteps);
                Console.WriteLine($"[harness] agent exit code: {agentRc}");
            }

            var testsProj = Path.Combine(repoRoot, "tests", "AmlAgent.Tests", "AmlAgent.Tests.csproj");
            Console.WriteLine($"\n[harness] running tests against workspace");
            var testRc = RunDotnetTest(testsProj, workspace);
            Console.WriteLine($"[harness] tests exit code: {testRc}");
            return testRc;
        }
        finally
        {
            if (keepWorkspace) Console.WriteLine($"[harness] workspace kept: {workspace}");
            else SafeDelete(workspace);
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("aml-harness — Docker-based benchmark runner");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  aml-harness [--agent <name>] [--task <id>] [--model <id>]");
        Console.WriteLine("              [--max-steps <n>] [--keep-workspace] [--oracle]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --agent           agent dir under agents/        (default: csharp-sk)");
        Console.WriteLine("  --task            task dir under tasks/          (default: aml-transaction-network)");
        Console.WriteLine("  --model           override BENCH_MODEL           (default: gpt-4o-mini)");
        Console.WriteLine("  --max-steps       cap on agent turns             (default: 25)");
        Console.WriteLine("  --keep-workspace  keep temp workspace on exit");
        Console.WriteLine("  --oracle          use AmlAgent.Oracle instead of running the agent container");
    }

    private static void StageWorkspace(string taskDir, string workspace)
    {
        var envSrc = Path.Combine(taskDir, "environment");
        foreach (var entry in Directory.GetFileSystemEntries(envSrc))
        {
            var name = Path.GetFileName(entry);
            var dest = Path.Combine(workspace, name);
            if (Directory.Exists(entry)) CopyDir(entry, dest);
            else File.Copy(entry, dest, overwrite: true);
        }
        File.Copy(Path.Combine(taskDir, "instruction.md"),
                  Path.Combine(workspace, "instruction.md"),
                  overwrite: true);
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    private static string BuildAgentImage(string agent, string agentDir)
    {
        var tag = $"aml-bench-agent-{agent}:latest";
        var rc = RunProcess("docker", new[] { "build", "-t", tag, agentDir });
        if (rc != 0) throw new InvalidOperationException("agent image build failed");
        return tag;
    }

    private static int RunAgentContainer(string image, string workspace, string apiKey, string? model, int maxSteps)
    {
        var dockerArgs = new List<string>
        {
            "run", "--rm",
            "-v", $"{workspace}:/app",
            "-e", $"OPENAI_API_KEY={apiKey}",
            "-e", $"BENCH_MAX_STEPS={maxSteps}",
            "-e", "BENCH_TASK_DIR=/app",
        };
        if (!string.IsNullOrEmpty(model))
        {
            dockerArgs.Add("-e");
            dockerArgs.Add($"BENCH_MODEL={model}");
        }
        dockerArgs.Add(image);
        return RunProcess("docker", dockerArgs);
    }

    private static int RunDotnetTest(string testsProj, string workspace)
    {
        var env = new Dictionary<string, string> { ["AML_BENCH_WORKSPACE"] = workspace };
        return RunProcess("dotnet", new[] { "test", testsProj, "--nologo", "-v", "minimal" }, env);
    }

    private static int RunProcess(string file, IEnumerable<string> args, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (env is not null) foreach (var (k, v) in env) psi.Environment[k] = v;

        Console.WriteLine($"$ {file} {string.Join(' ', args)}");
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AML-Agent-Bench.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static void SafeDelete(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
