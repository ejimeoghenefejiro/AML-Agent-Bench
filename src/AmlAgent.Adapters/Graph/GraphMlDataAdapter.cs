using System.Text;
using System.Xml.Linq;
using AmlAgent.Adapters.Canonical;

namespace AmlAgent.Adapters.Graph;

/// <summary>
/// Loads an entity graph from a GraphML (XML) file into canonical entities
/// and relationships. Looks up elements by local name only (not a strict
/// namespace match) so both properly-namespaced GraphML
/// (xmlns="http://graphml.graphdrawing.org/xmlns") and minimal
/// namespace-free XML in the same shape both parse -- tolerant of how a
/// given graph-export tool actually emits it.
///
/// Expected shape: &lt;node id="..."&gt;&lt;data key="label"&gt;.../&lt;data key="name"&gt;...
/// and &lt;edge id="..." source="..." target="..."&gt;&lt;data key="type"&gt;...&lt;data key="evidence_ids"&gt;comma,separated
/// </summary>
public sealed class GraphMlDataAdapter : IAmlDataAdapter
{
    public string AdapterId => "graphml";
    public string AdapterVersion => "1.0.0";

    public async Task<CanonicalAmlDataset> LoadAsync(DataSourceConfiguration source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
            throw new InvalidAdapterConfigurationException(AdapterId, "'Path' is required");
        if (!File.Exists(source.Path))
            throw new AdapterSourceException(AdapterId, $"file not found: {source.Path}");

        var bytes = await File.ReadAllBytesAsync(source.Path, cancellationToken);
        return LoadFromBytes(bytes, source.Path);
    }

    /// <summary>Testable without touching disk -- takes raw file bytes directly.</summary>
    public CanonicalAmlDataset LoadFromBytes(byte[] bytes, string sourcePathForLineage)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        }
        catch (Exception ex)
        {
            throw new AdapterSourceException(AdapterId, $"invalid GraphML/XML: {ex.Message}", ex);
        }

        var graph = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "graph");
        if (graph is null)
            throw new AdapterSourceException(AdapterId, "no <graph> element found under the document root");

        var sourceName = Path.GetFileName(sourcePathForLineage);
        var entities = new Dictionary<string, CanonicalEntity>(StringComparer.OrdinalIgnoreCase);
        var relationships = new List<CanonicalRelationship>();
        var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Elements().Where(e => e.Name.LocalName == "node"))
        {
            var id = (string?)node.Attribute("id")
                ?? throw new AdapterNormalisationException(AdapterId, "a <node> element is missing its 'id' attribute");
            var data = DataDictionary(node);

            entities[id] = new CanonicalEntity(
                EntityId: id,
                EntityType: data.GetValueOrDefault("label", "unknown"),
                DisplayName: data.GetValueOrDefault("name"),
                SourceLineage: new SourceLineage("graphml", sourceName, null, id, AdapterId, AdapterVersion));
        }

        foreach (var edge in graph.Elements().Where(e => e.Name.LocalName == "edge"))
        {
            var id = (string?)edge.Attribute("id") ?? $"edge-{relationships.Count + 1}";
            var sourceId = (string?)edge.Attribute("source")
                ?? throw new AdapterNormalisationException(AdapterId, $"edge '{id}' is missing its 'source' attribute");
            var targetId = (string?)edge.Attribute("target")
                ?? throw new AdapterNormalisationException(AdapterId, $"edge '{id}' is missing its 'target' attribute");

            if (!entities.ContainsKey(sourceId))
                throw new AdapterNormalisationException(AdapterId, $"edge '{id}' references unknown source node '{sourceId}'");
            if (!entities.ContainsKey(targetId))
                throw new AdapterNormalisationException(AdapterId, $"edge '{id}' references unknown target node '{targetId}'");
            if (!seenRelationshipIds.Add(id))
                throw new AdapterNormalisationException(AdapterId, $"duplicate edge id '{id}'");

            var data = DataDictionary(edge);
            var evidenceIds = data.TryGetValue("evidence_ids", out var ev)
                ? ev.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
                : new List<string>();

            relationships.Add(new CanonicalRelationship(
                RelationshipId: id,
                SourceEntityId: sourceId,
                TargetEntityId: targetId,
                RelationshipType: data.GetValueOrDefault("type", "related_to"),
                EvidenceIds: evidenceIds,
                SourceLineage: new SourceLineage("graphml", sourceName, null, id, AdapterId, AdapterVersion)));
        }

        return CanonicalAmlDataset.Empty() with
        {
            Entities = entities.Values.ToList(),
            Relationships = relationships,
        };
    }

    private static Dictionary<string, string> DataDictionary(XElement element) =>
        element.Elements().Where(e => e.Name.LocalName == "data")
            .Select(d => new { Key = (string?)d.Attribute("key"), Value = d.Value })
            .Where(x => x.Key is not null)
            .ToDictionary(x => x.Key!, x => x.Value, StringComparer.OrdinalIgnoreCase);
}
