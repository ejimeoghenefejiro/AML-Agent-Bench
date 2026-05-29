namespace AmlAgent.Harness;

/// <summary>
/// Tiny .env loader. Walks up from the current directory looking for a
/// <c>.env</c> file and loads <c>KEY=VALUE</c> pairs into the current
/// process environment (without overwriting variables that are already set).
/// Lines beginning with <c>#</c> and blank lines are ignored.
///
/// Mirrors <c>agents/csharp-sk/Config/DotEnv.cs</c> so the harness can pick
/// up <c>OPENAI_API_KEY</c> the same way the agent does, without forcing the
/// user to <c>set OPENAI_API_KEY=</c> in their shell.
/// </summary>
internal static class DotEnv
{
    public static string? Load()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
            {
                Apply(candidate);
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static void Apply(string path)
    {
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
