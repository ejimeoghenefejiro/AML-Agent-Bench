using AmlAgent.Evidence;
using Xunit;

namespace AmlAgent.ResearchValidation;

/// <summary>
/// v0.3 validation-priorities item 1: proves the frozen task-007-v1 annotator
/// package (validation/annotator-packages/task-007-v1/) actually excludes the
/// author's answer files, by construction, not just by documentation
/// discipline -- and that its template parses under the schema
/// AmlAgent.Evidence.GoldClaimAnnotationReader expects.
/// </summary>
public class AnnotatorPackageTests
{
    private static readonly string PackageDir = Path.Combine(
        AppContext.BaseDirectory, "validation", "annotator-packages", "task-007-v1");

    [Fact]
    public void Package_DoesNotContainAuthorAnswerFiles()
    {
        var allFiles = Directory.GetFiles(PackageDir, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The exact three files that would give away the answer -- see
        // validation/annotator-packages/task-007-v1/README.md's own
        // "What was deliberately excluded, and why" section.
        Assert.DoesNotContain("evidence-annotations.json", allFiles);
        Assert.DoesNotContain("expected-behaviour.md", allFiles);
        Assert.DoesNotContain("rubric.json", allFiles);
    }

    [Fact]
    public void Package_ContainsExpectedCaseDataFiles()
    {
        var caseDataDir = Path.Combine(PackageDir, "case_data");
        var files = Directory.GetFiles(caseDataDir).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in new[]
        {
            "transactions_primary.csv", "transactions_correspondent.json",
            "transactions_archive.parquet", "relationships.graphml",
            "case-notes.md", "case-definition.json",
        })
            Assert.Contains(expected, files);
    }

    [Fact]
    public void Package_ContainsInstructionsPromptAndTemplate()
    {
        Assert.True(File.Exists(Path.Combine(PackageDir, "INSTRUCTIONS.md")));
        Assert.True(File.Exists(Path.Combine(PackageDir, "prompt.md")));
        Assert.True(File.Exists(Path.Combine(PackageDir, "template.json")));
        Assert.True(File.Exists(Path.Combine(PackageDir, "README.md")));
    }

    [Fact]
    public void Template_IsSyntacticallyValidJson()
    {
        // Not a claim the template's example CONTENT is meaningful -- it's a
        // placeholder an annotator overwrites -- only that the file is valid
        // JSON an editor won't choke on, and that removing the one
        // non-schema "_instructions" field still leaves something the real
        // reader accepts (proving the schema shape itself, id est every
        // field name, is right).
        var raw = File.ReadAllText(Path.Combine(PackageDir, "template.json"));
        Assert.Contains("_instructions", raw); // present, and expected to be deleted before submission

        var withoutInstructionsField = System.Text.Json.Nodes.JsonNode.Parse(raw)!.AsObject();
        withoutInstructionsField.Remove("_instructions");

        var set = GoldClaimAnnotationReader.Parse(withoutInstructionsField.ToJsonString());
        Assert.Equal("task-007-multi-source-mule-network", set.TaskId);
        Assert.Equal(GoldClaimAnnotationReader.CurrentSchemaVersion, set.SchemaVersion);
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AML-Agent-Bench.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void CaseDataFiles_MatchTheRealTaskEnvironment_NoDrift()
    {
        // The package is a frozen COPY -- if the real task's environment
        // data ever changes, this test fails loudly rather than the package
        // silently drifting out of sync with what an agent actually sees.
        // Only "validation/" is copied to this test project's output, so the
        // real task files are read directly from the repo root, walked up to
        // from the test binary's own location (same pattern JudgeAgent.FindRubric uses).
        var repoRoot = FindRepoRoot();
        Skip.If(repoRoot is null, "could not locate repo root (AML-Agent-Bench.sln) above the test binary");

        var realDataDir = Path.Combine(repoRoot!, "tasks", "task-007-multi-source-mule-network", "environment", "data");

        foreach (var fileName in new[] { "transactions_primary.csv", "transactions_correspondent.json", "relationships.graphml", "case-notes.md" })
        {
            var real = File.ReadAllBytes(Path.Combine(realDataDir, fileName));
            var packaged = File.ReadAllBytes(Path.Combine(PackageDir, "case_data", fileName));
            Assert.Equal(real, packaged);
        }
    }
}
