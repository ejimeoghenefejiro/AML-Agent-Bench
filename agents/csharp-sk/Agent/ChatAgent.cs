using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AmlAgent.Agent;

internal static class ChatAgent
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? taskId = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--task" && i + 1 < args.Length) { taskId = args[++i]; }
        }

        var sandbox = Environment.GetEnvironmentVariable("BENCH_TASK_DIR") ?? Directory.GetCurrentDirectory();
        var kernel = KernelFactory.BuildKernel(sandbox, out var model);
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.2,
        };

        var history = new ChatHistory();
        history.AddSystemMessage(KernelFactory.SystemPrompt +
            "\n\nYou are now running in an interactive CMD REPL. The user will chat with you across multiple turns. " +
            "Tools still work — feel free to inspect files, run shell commands, and demonstrate behaviour.");

        if (taskId is not null)
        {
            var instructionPath = FindTaskInstruction(taskId)
                ?? throw new FileNotFoundException($"Could not locate instruction.md for task '{taskId}'");
            var instruction = await File.ReadAllTextAsync(instructionPath);
            history.AddUserMessage(
                $"[Pre-loaded task: {taskId}] The instructions below are loaded into context but you do NOT need to solve the task " +
                "immediately. Wait for the user to ask questions or to start.\n\n" +
                "----- BEGIN INSTRUCTIONS -----\n" + instruction + "\n----- END INSTRUCTIONS -----");
            Console.WriteLine($"[chat] pre-loaded task: {taskId}");
        }

        PrintBanner(model, sandbox, taskId);

        while (true)
        {
            Console.Write("\nyou> ");
            var line = Console.ReadLine();
            if (line is null) break;
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line is "/exit" or "/quit") break;
            if (line is "/reset")
            {
                history.Clear();
                history.AddSystemMessage(KernelFactory.SystemPrompt);
                Console.WriteLine("[chat] history reset");
                continue;
            }
            if (line is "/help")
            {
                Console.WriteLine("/exit  /quit   leave the chat");
                Console.WriteLine("/reset         clear conversation history");
                Console.WriteLine("/help          show this");
                continue;
            }

            history.AddUserMessage(line);
            try
            {
                var response = await chat.GetChatMessageContentAsync(history, settings, kernel);
                var text = response.Content ?? "";
                Console.WriteLine($"\nagent> {text}");
                history.AddAssistantMessage(text);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
            }
        }
        return 0;
    }

    private static void PrintBanner(string model, string sandbox, string? taskId)
    {
        Console.WriteLine("============================================================");
        Console.WriteLine(" AmlAgent — interactive CMD chat (C# + Semantic Kernel)");
        Console.WriteLine($" model:   {model}");
        Console.WriteLine($" sandbox: {sandbox}");
        if (taskId is not null) Console.WriteLine($" task:    {taskId}");
        Console.WriteLine(" commands: /exit  /reset  /help");
        Console.WriteLine("============================================================");
    }

    private static string? FindTaskInstruction(string taskId)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tasks", taskId, "instruction.md");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
