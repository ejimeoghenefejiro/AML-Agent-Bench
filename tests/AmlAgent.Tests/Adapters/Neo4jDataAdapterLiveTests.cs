using AmlAgent.Adapters;
using AmlAgent.Adapters.Configuration;
using AmlAgent.Adapters.Graph;
using Xunit;

namespace AmlAgent.Tests.Adapters;

/// <summary>
/// Genuine end-to-end tests against a real Neo4j instance -- not a mock.
/// Skipped (not failed) when no live graph database is configured, same
/// SkippableFact pattern as PostgreSqlDataAdapterLiveTests.
///
/// Verified live on 2026-08-08 against neo4j:5-community in a temporary
/// Docker container, seeded with a mule-network-style chain
/// (A100 -[transferred_to]-> A812 -[transferred_to]-> A900), matching the
/// CLI-Only spec's own worked graph example.
/// </summary>
public class Neo4jDataAdapterLiveTests
{
    private const string ProfileName = "test-neo4j";

    private static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionProfileResolver.EnvVarNameFor(ProfileName)));

    [SkippableFact]
    public async Task LoadAsync_RealNeo4j_LoadsEntitiesAndRelationships()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live Neo4j connection string to run this");

        var adapter = new Neo4jDataAdapter();
        var source = new DataSourceConfiguration("neo4j", ConnectionProfile: ProfileName);
        var dataset = await adapter.LoadAsync(source);

        Assert.Equal(3, dataset.Entities.Count); // A100, A812, A900
        Assert.Equal(2, dataset.Relationships.Count); // A100->A812, A812->A900
    }

    [SkippableFact]
    public async Task LoadAsync_RealNeo4j_MapsRelationshipTypeAndEvidenceIds()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live Neo4j connection string to run this");

        var adapter = new Neo4jDataAdapter();
        var source = new DataSourceConfiguration("neo4j", ConnectionProfile: ProfileName);
        var dataset = await adapter.LoadAsync(source);

        var r1 = dataset.Relationships.Single(r => r.RelationshipId == "R-1001");
        Assert.Equal("transferred_to", r1.RelationshipType);
        Assert.Equal("A100", r1.SourceEntityId);
        Assert.Equal("A812", r1.TargetEntityId);
        Assert.Contains("T10021", r1.EvidenceIds);

        var r2 = dataset.Relationships.Single(r => r.RelationshipId == "R-1002");
        Assert.Equal(2, r2.EvidenceIds.Count);
        Assert.Contains("T10022", r2.EvidenceIds);
        Assert.Contains("T10023", r2.EvidenceIds);
    }

    [SkippableFact]
    public async Task LoadAsync_RealNeo4j_MapsNodeLabelAsEntityType()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live Neo4j connection string to run this");

        var adapter = new Neo4jDataAdapter();
        var source = new DataSourceConfiguration("neo4j", ConnectionProfile: ProfileName);
        var dataset = await adapter.LoadAsync(source);

        var a100 = dataset.Entities.Single(e => e.EntityId == "A100");
        Assert.Equal("Account", a100.EntityType);
        Assert.Equal("Victim Account", a100.DisplayName);
    }

    [SkippableFact]
    public async Task LoadAsync_RealNeo4j_RecordsSourceLineage()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live Neo4j connection string to run this");

        var adapter = new Neo4jDataAdapter();
        var source = new DataSourceConfiguration("neo4j", ConnectionProfile: ProfileName);
        var dataset = await adapter.LoadAsync(source);

        var lineage = dataset.Relationships[0].SourceLineage;
        Assert.Equal("neo4j", lineage.SourceType);
        Assert.Equal("neo4j", lineage.Adapter);
    }

    [SkippableFact]
    public async Task LoadAsync_RealNeo4j_CustomCypherQuery_IsRespected()
    {
        Skip.IfNot(IsConfigured, $"set {ConnectionProfileResolver.EnvVarNameFor(ProfileName)} to a live Neo4j connection string to run this");

        var adapter = new Neo4jDataAdapter();
        var source = new DataSourceConfiguration("neo4j", ConnectionProfile: ProfileName,
            Query: "MATCH (a)-[r]->(b) WHERE a.id = 'A100' RETURN a, r, b");
        var dataset = await adapter.LoadAsync(source);

        Assert.Single(dataset.Relationships);
        Assert.Equal("A100", dataset.Relationships[0].SourceEntityId);
    }

    [SkippableFact]
    public async Task LoadAsync_RealNeo4j_BadConnectionProfile_ThrowsClearError()
    {
        Skip.IfNot(IsConfigured, "requires the driver to actually attempt a connection, so still gated on Neo4j being reachable in this environment");

        var adapter = new Neo4jDataAdapter();
        var source = new DataSourceConfiguration("neo4j", ConnectionProfile: $"nonexistent-{Guid.NewGuid():N}");
        await Assert.ThrowsAsync<InvalidAdapterConfigurationException>(() => adapter.LoadAsync(source));
    }
}
