using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AmlAgent.Agent;

/// <summary>
/// Retries a chat-completion call on transient network failures (timeouts,
/// dropped connections) so a single blip on a ~30-second HttpClient timeout
/// doesn't crash the whole benchmark run with an unhandled-exception stack
/// dump. Real (non-transient) failures — bad API key, malformed request,
/// content-policy rejection — are not retried and propagate immediately.
/// </summary>
internal static class RetryHelper
{
    public static async Task<ChatMessageContent> GetChatMessageContentWithRetryAsync(
        this IChatCompletionService chat,
        ChatHistory history,
        PromptExecutionSettings settings,
        Kernel kernel,
        string label,
        int maxAttempts = 3)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await chat.GetChatMessageContentAsync(history, settings, kernel);
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                var delay = TimeSpan.FromSeconds(attempt * 5);
                Console.Error.WriteLine($"[{label}] transient network error on attempt {attempt}/{maxAttempts} ({ExceptionSummary(ex)}); retrying in {delay.TotalSeconds:0}s...");
                await Task.Delay(delay);
            }
        }

        // Final attempt: let any exception propagate with its real type/message.
        return await chat.GetChatMessageContentAsync(history, settings, kernel);
    }

    private static bool IsTransient(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is TaskCanceledException
                or TimeoutException
                or System.Net.Http.HttpRequestException
                or System.Net.Sockets.SocketException
                or IOException)
                return true;
        }
        return false;
    }

    private static string ExceptionSummary(Exception ex)
    {
        var e = ex;
        while (e.InnerException is not null) e = e.InnerException;
        return $"{e.GetType().Name}: {e.Message}";
    }
}
