using AmlAgent.Agent;
using AmlAgent.Config;

namespace AmlAgent;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var envFile = DotEnv.Load();
        if (envFile is not null)
            Console.WriteLine($"[env] loaded {envFile}");

        if (args.Length == 0)
        {
            PrintUsage();
            return 64;
        }

        var cmd = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        return cmd switch
        {
            "run"   => await BenchmarkAgent.RunAsync(rest),
            "chat"  => await ChatAgent.RunAsync(rest),
            "judge" => await JudgeAgent.RunAsync(rest),
            "-h" or "--help" or "help" => Help(),
            _ => Unknown(cmd),
        };
    }

    private static int Help() { PrintUsage(); return 0; }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        PrintUsage();
        return 64;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("aml-agent — C# / Semantic Kernel agent for AML-Agent-Bench");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  aml-agent run                                     Run the benchmark loop (reads instruction.md or prompt.md)");
        Console.WriteLine("  aml-agent chat [--task <id>]                      Interactive CMD chat REPL for local testing");
        Console.WriteLine("  aml-agent judge --task <id> --workspace <path>    Score a benchmark workspace via LLM-as-judge rubric");
        Console.WriteLine();
        Console.WriteLine("Environment:");
        Console.WriteLine("  OPENAI_API_KEY    (required)");
        Console.WriteLine("  BENCH_MODEL       default: gpt-4o-mini");
        Console.WriteLine("  BENCH_TASK_DIR    default: /app (run mode) or current dir (chat mode)");
        Console.WriteLine("  BENCH_MAX_STEPS   default: 25");
    }
}
