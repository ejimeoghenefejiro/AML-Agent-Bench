namespace AmlAgent.Adapters.Canonical;

/// <summary>
/// Traces one canonical record back to exactly where it came from:
/// Canonical Record -&gt; Normalised Source -&gt; Original Source Record.
/// Every canonical record carries one of these -- there is no such thing as
/// an un-traceable canonical record in this model.
/// </summary>
public sealed record SourceLineage(
    string SourceType,
    string? SourceName,
    string? Table,
    string SourceRecordId,
    string Adapter,
    string AdapterVersion);
