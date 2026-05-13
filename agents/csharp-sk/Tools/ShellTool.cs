using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.SemanticKernel;

namespace AmlAgent.Tools;

public sealed class ShellTool
{
    private readonly string _cwd;

    public ShellTool(string cwd) => _cwd = cwd;

    [KernelFunction, Description("Run a shell command (bash -lc) inside the sandbox and return combined stdout/stderr and exit code. Use for `python`, `ls`, `cat`, `pytest`, etc.")]
    public async Task<string> Run(
        [Description("Shell command to execute")] string command,
        [Description("Timeout in seconds")] int timeoutSec = 120)
    {
        var psi = new ProcessStartInfo("bash", $"-lc \"{command.Replace("\"", "\\\"")}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _cwd,
        };

        using var proc = Process.Start(psi)!;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(timeoutSec * 1000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return $"TIMEOUT after {timeoutSec}s";
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var sb = new StringBuilder();
        sb.AppendLine($"exit_code={proc.ExitCode}");
        if (stdout.Length > 0) { sb.AppendLine("--- stdout ---"); sb.AppendLine(stdout); }
        if (stderr.Length > 0) { sb.AppendLine("--- stderr ---"); sb.AppendLine(stderr); }
        var result = sb.ToString();
        return result.Length > 50_000 ? result[..50_000] + "\n...[truncated]" : result;
    }
}
