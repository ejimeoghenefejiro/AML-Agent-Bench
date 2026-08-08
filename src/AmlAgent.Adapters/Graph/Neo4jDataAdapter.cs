using AmlAgent.Adapters.Canonical;
using AmlAgent.Adapters.Configuration;
using Neo4j.Driver;

namespace AmlAgent.Adapters.Graph;

/// <summary>
/// Loads an entity graph from Neo4j into canonical entities and
/// relationships (CLI-Only spec section 18: useful for mule networks,
/// layering, shell-company relationships, beneficiary networks, corporate
/// ownership, device-sharing patterns). Default query returns every
/// (node)-[relationship]->(node) triple; override with a custom Cypher
/// query via DataSourceConfiguration.Query as long as it aliases the
/// pattern as "a", "r", "b".
/// </summary>
public sealed class Neo4jDataAdapter : IAmlDataAdapter
{
    private const string DefaultQuery = "MATCH (a)-[r]->(b) RETURN a, r, b";

    public string AdapterId => "neo4j";
    public string AdapterVersion => "1.0.0";

    public async Task<CanonicalAmlDataset> LoadAsync(DataSourceConfiguration source, CancellationToken cancellationToken = default)
    {
        var connectionString = ConnectionProfileResolver.Resolve(source.ConnectionProfile, AdapterId);
        var (uri, user, password) = ParseConnectionString(connectionString);
        var query = string.IsNullOrWhiteSpace(source.Query) ? DefaultQuery : source.Query;

        IDriver driver;
        try
        {
            driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
        }
        catch (Exception ex)
        {
            throw new AdapterSourceException(AdapterId, $"could not create Neo4j driver for profile '{source.ConnectionProfile}': {ex.Message}", ex);
        }

        await using (driver)
        {
            await using var session = driver.AsyncSession();

            List<IRecord> records;
            try
            {
                var cursor = await session.RunAsync(query);
                records = await cursor.ToListAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                throw new AdapterSourceException(AdapterId, $"Neo4j query failed: {ex.Message}", ex);
            }

            var entities = new Dictionary<string, CanonicalEntity>(StringComparer.OrdinalIgnoreCase);
            var relationships = new List<CanonicalRelationship>();
            var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in records)
            {
                if (!record.Values.TryGetValue("a", out var aObj) || aObj is not INode a)
                    throw new AdapterNormalisationException(AdapterId, "query result is missing a node aliased 'a' -- see DefaultQuery pattern");
                if (!record.Values.TryGetValue("b", out var bObj) || bObj is not INode b)
                    throw new AdapterNormalisationException(AdapterId, "query result is missing a node aliased 'b' -- see DefaultQuery pattern");
                if (!record.Values.TryGetValue("r", out var rObj) || rObj is not IRelationship r)
                    throw new AdapterNormalisationException(AdapterId, "query result is missing a relationship aliased 'r' -- see DefaultQuery pattern");

                var entityA = ToCanonicalEntity(a, source.ConnectionProfile);
                var entityB = ToCanonicalEntity(b, source.ConnectionProfile);
                entities[entityA.EntityId] = entityA;
                entities[entityB.EntityId] = entityB;

                var relationshipId = PropertyOrDefault(r.Properties, "id") ?? r.ElementId;
                if (seenRelationshipIds.Add(relationshipId))
                {
                    var evidenceIds = r.Properties.TryGetValue("evidence_ids", out var evObj) && evObj is IEnumerable<object> evList
                        ? evList.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList()
                        : new List<string>();

                    relationships.Add(new CanonicalRelationship(
                        RelationshipId: relationshipId,
                        SourceEntityId: entityA.EntityId,
                        TargetEntityId: entityB.EntityId,
                        RelationshipType: r.Type,
                        EvidenceIds: evidenceIds,
                        SourceLineage: new SourceLineage("neo4j", source.ConnectionProfile, null, relationshipId, AdapterId, AdapterVersion)));
                }
            }

            return CanonicalAmlDataset.Empty() with
            {
                Entities = entities.Values.ToList(),
                Relationships = relationships,
            };
        }
    }

    private CanonicalEntity ToCanonicalEntity(INode node, string? sourceName)
    {
        var entityId = PropertyOrDefault(node.Properties, "id") ?? node.ElementId;
        var entityType = node.Labels.FirstOrDefault() ?? "unknown";
        var displayName = PropertyOrDefault(node.Properties, "name") ?? PropertyOrDefault(node.Properties, "display_name");

        return new CanonicalEntity(
            EntityId: entityId,
            EntityType: entityType,
            DisplayName: displayName,
            SourceLineage: new SourceLineage("neo4j", sourceName, null, entityId, AdapterId, AdapterVersion));
    }

    private static string? PropertyOrDefault(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) ? value?.ToString() : null;

    /// <summary>
    /// Connection profiles for Neo4j hold "Uri=bolt://host:port;User=neo4j;Password=...",
    /// the same key=value convention as the SQL adapters' connection strings,
    /// so the security boundary (env-var only, never committed) stays uniform
    /// across every adapter type.
    /// </summary>
    private (string Uri, string User, string Password) ParseConnectionString(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(kv => kv.Split('=', 2))
            .Where(kv => kv.Length == 2)
            .ToDictionary(kv => kv[0].Trim(), kv => kv[1].Trim(), StringComparer.OrdinalIgnoreCase);

        if (!parts.TryGetValue("Uri", out var uri))
            throw new InvalidAdapterConfigurationException(AdapterId, "connection string must include 'Uri=bolt://host:port'");

        var user = parts.GetValueOrDefault("User", "neo4j");
        var password = parts.GetValueOrDefault("Password", "");
        return (uri, user, password);
    }
}
