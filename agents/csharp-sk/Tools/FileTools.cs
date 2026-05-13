using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace AmlAgent.Tools;

public sealed class FileTools
{
    private readonly string _root;

    public FileTools(string root) => _root = Path.GetFullPath(root);

    private string Resolve(string path)
    {
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_root, path));
        return full;
    }

    [KernelFunction, Description("List files in a directory inside the sandbox.")]
    public string ListDir(
        [Description("Absolute or sandbox-relative directory path")] string path)
    {
        var full = Resolve(path);
        if (!Directory.Exists(full)) return $"NOT_A_DIRECTORY: {full}";
        var entries = Directory.EnumerateFileSystemEntries(full)
            .Select(p => Directory.Exists(p) ? p + "/" : p);
        return string.Join("\n", entries);
    }

    [KernelFunction, Description("Read a UTF-8 text file.")]
    public async Task<string> ReadFile(
        [Description("File path inside the sandbox")] string path,
        [Description("Optional max characters to return")] int maxChars = 200_000)
    {
        var full = Resolve(path);
        if (!File.Exists(full)) return $"FILE_NOT_FOUND: {full}";
        var content = await File.ReadAllTextAsync(full);
        return content.Length <= maxChars ? content : content[..maxChars] + "\n...[truncated]";
    }

    [KernelFunction, Description("Write UTF-8 text to a file (overwrites). Use this to author code or output files.")]
    public async Task<string> WriteFile(
        [Description("File path inside the sandbox")] string path,
        [Description("Full file contents")] string content)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);
        return $"WROTE {content.Length} chars to {full}";
    }
}
