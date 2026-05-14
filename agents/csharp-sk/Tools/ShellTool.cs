using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.SemanticKernel;

namespace AmlAgent.Tools;

public sealed class ShellTool
{
    private readonly string _cwd;

    public ShellTool(string cwd) => _cwd = cwd;

    [KernelFunction, Description("Run a shell command in the sandbox and return combined stdout/stderr + exit code. Examples: `dotnet script solve.csx`, `ls`, `cat file`.")]
    public async Task<string> Run(
        [Description("Shell command to execute")] string command,
        [Description("Timeout in seconds")] int timeoutSec = 180)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _cwd,
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            psi.FileName = "cmd.exe";
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi.FileName = "/bin/bash";
            psi.ArgumentList.Add("-lc");
            psi.ArgumentList.Add(command);
        }

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
