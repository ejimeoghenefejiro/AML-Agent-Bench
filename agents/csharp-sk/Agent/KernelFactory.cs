using AmlAgent.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace AmlAgent.Agent;

internal static class KernelFactory
{
    public const string SystemPrompt =
        "You are an autonomous benchmark agent for AML-Agent-Bench, written in C# with Microsoft Semantic Kernel. " +
        "You operate inside a sandboxed Linux container. Your working directory is the sandbox root (BENCH_TASK_DIR). " +
        "You have these tools: files.ListDir, files.ReadFile, files.WriteFile, and shell.Run. " +
        "When solving tasks prefer C# (dotnet-script *.csx files are runnable in the sandbox via `dotnet script file.csx`). " +
        "Only fall back to Python if a C# approach is impractical. " +
        "Always read instruction.md carefully, plan, author code, execute it, verify outputs against the rules, " +
        "and write the final required output file at the exact path the instructions specify. " +
        "When the task is complete and you have verified the output, reply with the single token DONE.";

    public static Kernel BuildKernel(string sandboxRoot, out string model)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY is not set");
        model = Environment.GetEnvironmentVariable("BENCH_MODEL") ?? "gpt-4o-mini";

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId: model, apiKey: apiKey);
        builder.Services.AddLogging(c => c.AddConsole().SetMinimumLevel(LogLevel.Warning));
        builder.Plugins.AddFromObject(new FileTools(sandboxRoot), "files");
        builder.Plugins.AddFromObject(new ShellTool(sandboxRoot), "shell");
        return builder.Build();
    }
}
