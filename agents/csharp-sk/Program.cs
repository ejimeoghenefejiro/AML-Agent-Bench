using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using AmlAgent.Tools;

namespace AmlAgent;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var taskDir = Environment.GetEnvironmentVariable("BENCH_TASK_DIR") ?? "/app";
        var instructionPath = Path.Combine(taskDir, "instruction.md");
        if (!File.Exists(instructionPath))
        {
            Console.Error.WriteLine($"Missing instruction file: {instructionPath}");
            return 2;
        }

        var instruction = await File.ReadAllTextAsync(instructionPath);

        var model = Environment.GetEnvironmentVariable("BENCH_MODEL") ?? "gpt-4o-mini";
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException("OPENAI_API_KEY not set");
        var maxSteps = int.Parse(Environment.GetEnvironmentVariable("BENCH_MAX_STEPS") ?? "25");

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId: model, apiKey: apiKey);
        builder.Services.AddLogging(c => c.AddConsole().SetMinimumLevel(LogLevel.Warning));
        builder.Plugins.AddFromObject(new FileTools(taskDir), "files");
        builder.Plugins.AddFromObject(new ShellTool(taskDir), "shell");

        var kernel = builder.Build();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.0,
        };

        var history = new ChatHistory();
        history.AddSystemMessage(
            "You are a benchmark agent solving a regulated FinTech task inside a sandboxed Linux container. " +
            $"Your working directory is {taskDir}. Use the provided tools (files.*, shell.*) to read inputs, " +
            "write code, execute it, and produce the exact required output file. " +
            "When the task is complete and validated, reply with the single token DONE."
        );
        history.AddUserMessage(
            "Task instructions follow. Read carefully and produce the required output file.\n\n" +
            "----- BEGIN INSTRUCTIONS -----\n" + instruction + "\n----- END INSTRUCTIONS -----"
        );

        for (int step = 1; step <= maxSteps; step++)
        {
            Console.WriteLine($"--- step {step} ---");
            var response = await chat.GetChatMessageContentAsync(history, settings, kernel);
            var text = response.Content ?? "";
            Console.WriteLine(text);
            history.AddAssistantMessage(text);

            if (text.Contains("DONE", StringComparison.Ordinal))
            {
                Console.WriteLine("Agent reported completion.");
                return 0;
            }
        }

        Console.Error.WriteLine($"Agent exhausted {maxSteps} steps without DONE.");
        return 1;
    }
}
