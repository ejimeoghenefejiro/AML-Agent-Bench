using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AmlAgent.Agent;

internal static class BenchmarkAgent
{
    public static async Task<int> RunAsync(string[] args)
    {
        var taskDir = Environment.GetEnvironmentVariable("BENCH_TASK_DIR") ?? "/app";
        var maxSteps = int.Parse(Environment.GetEnvironmentVariable("BENCH_MAX_STEPS") ?? "25");
        var instructionPath = Path.Combine(taskDir, "instruction.md");
        if (!File.Exists(instructionPath))
        {
            Console.Error.WriteLine($"Missing instruction file: {instructionPath}");
            return 2;
        }

        var instruction = await File.ReadAllTextAsync(instructionPath);
        var kernel = KernelFactory.BuildKernel(taskDir, out var model);
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.0,
        };

        var history = new ChatHistory();
        history.AddSystemMessage(KernelFactory.SystemPrompt);
        history.AddUserMessage(
            "Sandbox root: " + taskDir + "\n\n" +
            "Task instructions follow. Produce the required output file.\n\n" +
            "----- BEGIN INSTRUCTIONS -----\n" + instruction + "\n----- END INSTRUCTIONS -----");

        Console.WriteLine($"[benchmark] model={model} sandbox={taskDir} max_steps={maxSteps}");
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
