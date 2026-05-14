using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AmlAgent.Agent;

/// <summary>
/// LLM-as-judge subcommand. Loads a task rubric, the candidate's outputs from
/// a benchmark workspace and the grounding data, then asks the Semantic Kernel
/// chat model to score the candidate against each rubric dimension and emit
/// structured JSON. The result is written to &lt;workspace&gt;/judge_report.json
/// and is also validated by xUnit in tests/AmlAgent.Tests/JudgeReportTests.cs.
///
/// Used by AML-Agent-Bench to score qualitative aspects of regulatory output:
/// evidence citation, temporal reasoning, anomaly detection, fact/assumption
/// separation, compliance tone, and absence of unsupported claims.
/// </summary>
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
            "You MUST respond with a single JSON object and nothing else, matching the requested schema. " +
            "Penalise unsupported claims, accusatory language, and invented evidence. Reward cautious, " +
            "evidence-citing, regulator-friendly writing.";

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
          "verdict": "<PASS or FAIL>"
        }
        """);

        var history = new ChatHistory();
        history.AddSystemMessage(system);
        history.AddUserMessage(user.ToString());

        Console.WriteLine($"[judge] model={model} rubric={rubricPath}");
        var response = await chat.GetChatMessageContentAsync(history, settings, kernel);
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
        int overallScore = 0, overallMax = 0;
        var scores = parsed["scores"]?.AsObject();
        if (scores is not null)
        {
            foreach (var (_, node) in scores)
            {
                overallScore += (int?)node?["score"] ?? 0;
                overallMax += (int?)node?["max"] ?? 0;
            }
        }
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

        var outPath = Path.Combine(workspace, "judge_report.json");
        var finalJson = parsed.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outPath, finalJson);

        Console.WriteLine();
        Console.WriteLine($"[judge] wrote {outPath}");
        Console.WriteLine($"[judge] overall: {overallScore}/{overallMax} = {percentage:P1}");
        Console.WriteLine($"[judge] verdict: {verdict} (threshold {passThreshold:P0})");
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
}
