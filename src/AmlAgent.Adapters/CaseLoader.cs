using AmlAgent.Adapters.Canonical;

namespace AmlAgent.Adapters;

/// <summary>
/// Orchestrates a CaseDefinition end to end: resolve each source's adapter,
/// load it, merge every successfully-loaded source via CanonicalCaseMerger,
/// then validate cross-source evidence references via
/// EvidenceIntegrityValidator. A source that fails to load does not abort
/// the whole case -- it's recorded in Failures (never silently dropped) and
/// the case is still built from whatever did load, so a caller (CLI,
/// assurance wiring) can decide how to react to a partial case rather than
/// losing everything because one connection was unavailable.
/// </summary>
public static class CaseLoader
{
    public static async Task<CaseLoadResult> LoadAsync(
        CaseDefinition definition,
        AdapterRegistry registry,
        CancellationToken cancellationToken = default)
    {
        var datasets = new List<CanonicalAmlDataset>();
        var failures = new List<CaseSourceLoadFailure>();

        foreach (var src in definition.Sources)
        {
            IAmlDataAdapter adapter;
            try
            {
                adapter = registry.Resolve(src.SourceType);
            }
            catch (UnsupportedSourceTypeException ex)
            {
                failures.Add(new CaseSourceLoadFailure(src.SourceType, src.Role, ex.Message));
                continue;
            }

            try
            {
                var dataset = await adapter.LoadAsync(src.ToDataSourceConfiguration(), cancellationToken);
                datasets.Add(dataset);
            }
            catch (AdapterException ex)
            {
                failures.Add(new CaseSourceLoadFailure(src.SourceType, src.Role, ex.Message));
            }
        }

        var merged = CanonicalCaseMerger.Merge(datasets);
        var integrity = EvidenceIntegrityValidator.Validate(merged);

        return new CaseLoadResult(definition, datasets, merged, integrity, failures);
    }
}

public sealed record CaseLoadResult(
    CaseDefinition Definition,
    IReadOnlyList<CanonicalAmlDataset> SourceDatasets,
    CanonicalAmlCase MergedCase,
    EvidenceIntegrityResult EvidenceIntegrity,
    IReadOnlyList<CaseSourceLoadFailure> Failures)
{
    public bool AllSourcesLoaded => Failures.Count == 0;
}

/// <summary>One source that couldn't be loaded into the case -- recorded, never silently skipped.</summary>
public sealed record CaseSourceLoadFailure(string SourceType, string? Role, string ErrorMessage);
