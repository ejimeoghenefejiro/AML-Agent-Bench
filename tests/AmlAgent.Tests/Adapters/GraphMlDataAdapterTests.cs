using System.Text;
using AmlAgent.Adapters;
using AmlAgent.Adapters.Graph;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class GraphMlDataAdapterTests
{
    // Mirrors the CLI-Only spec's own worked mule-network example.
    private const string SampleGraphMl = """
    <?xml version="1.0" encoding="UTF-8"?>
    <graphml xmlns="http://graphml.graphdrawing.org/xmlns">
      <graph id="G" edgedefault="directed">
        <node id="A100"><data key="label">Account</data><data key="name">Victim Account</data></node>
        <node id="A812"><data key="label">Account</data><data key="name">Mule Account B</data></node>
        <node id="A900"><data key="label">Account</data><data key="name">Mule Account C</data></node>
        <edge id="R-1001" source="A100" target="A812"><data key="type">transferred_to</data><data key="evidence_ids">T10021</data></edge>
        <edge id="R-1002" source="A812" target="A900"><data key="type">transferred_to</data><data key="evidence_ids">T10022,T10023</data></edge>
      </graph>
    </graphml>
    """;

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void LoadFromBytes_ParsesAllNodesAndEdges()
    {
        var adapter = new GraphMlDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleGraphMl), "mule-network.graphml");

        Assert.Equal(3, dataset.Entities.Count);
        Assert.Equal(2, dataset.Relationships.Count);
    }

    [Fact]
    public void LoadFromBytes_MapsNodeLabelAndNameCorrectly()
    {
        var adapter = new GraphMlDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleGraphMl), "mule-network.graphml");

        var a100 = dataset.Entities.Single(e => e.EntityId == "A100");
        Assert.Equal("Account", a100.EntityType);
        Assert.Equal("Victim Account", a100.DisplayName);
    }

    [Fact]
    public void LoadFromBytes_MapsEdgeTypeAndEvidenceIds()
    {
        var adapter = new GraphMlDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleGraphMl), "mule-network.graphml");

        var r1002 = dataset.Relationships.Single(r => r.RelationshipId == "R-1002");
        Assert.Equal("A812", r1002.SourceEntityId);
        Assert.Equal("A900", r1002.TargetEntityId);
        Assert.Equal("transferred_to", r1002.RelationshipType);
        Assert.Equal(2, r1002.EvidenceIds.Count);
        Assert.Contains("T10022", r1002.EvidenceIds);
        Assert.Contains("T10023", r1002.EvidenceIds);
    }

    [Fact]
    public void LoadFromBytes_RecordsSourceLineage()
    {
        var adapter = new GraphMlDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(SampleGraphMl), "mule-network.graphml");
        var lineage = dataset.Entities.First().SourceLineage;
        Assert.Equal("graphml", lineage.SourceType);
        Assert.Equal("mule-network.graphml", lineage.SourceName);
    }

    [Fact]
    public void LoadFromBytes_EdgeReferencingUnknownNode_ThrowsAdapterNormalisationException()
    {
        const string bad = """
        <graphml xmlns="http://graphml.graphdrawing.org/xmlns">
          <graph id="G" edgedefault="directed">
            <node id="A100"><data key="label">Account</data></node>
            <edge id="R-1" source="A100" target="A999"><data key="type">transferred_to</data></edge>
          </graph>
        </graphml>
        """;
        var adapter = new GraphMlDataAdapter();
        Assert.Throws<AdapterNormalisationException>(() => adapter.LoadFromBytes(Bytes(bad), "bad.graphml"));
    }

    [Fact]
    public void LoadFromBytes_DuplicateEdgeId_ThrowsAdapterNormalisationException()
    {
        const string bad = """
        <graphml xmlns="http://graphml.graphdrawing.org/xmlns">
          <graph id="G" edgedefault="directed">
            <node id="A100"><data key="label">Account</data></node>
            <node id="A200"><data key="label">Account</data></node>
            <edge id="R-1" source="A100" target="A200"/>
            <edge id="R-1" source="A200" target="A100"/>
          </graph>
        </graphml>
        """;
        var adapter = new GraphMlDataAdapter();
        Assert.Throws<AdapterNormalisationException>(() => adapter.LoadFromBytes(Bytes(bad), "bad.graphml"));
    }

    [Fact]
    public void LoadFromBytes_NodeMissingIdAttribute_ThrowsAdapterNormalisationException()
    {
        const string bad = """
        <graphml xmlns="http://graphml.graphdrawing.org/xmlns">
          <graph id="G" edgedefault="directed">
            <node><data key="label">Account</data></node>
          </graph>
        </graphml>
        """;
        var adapter = new GraphMlDataAdapter();
        Assert.Throws<AdapterNormalisationException>(() => adapter.LoadFromBytes(Bytes(bad), "bad.graphml"));
    }

    [Fact]
    public void LoadFromBytes_MalformedXml_ThrowsAdapterSourceException()
    {
        var adapter = new GraphMlDataAdapter();
        Assert.Throws<AdapterSourceException>(() => adapter.LoadFromBytes(Bytes("<not-closed"), "bad.graphml"));
    }

    [Fact]
    public void LoadFromBytes_NoGraphElement_ThrowsAdapterSourceException()
    {
        var adapter = new GraphMlDataAdapter();
        Assert.Throws<AdapterSourceException>(() => adapter.LoadFromBytes(Bytes("<graphml></graphml>"), "bad.graphml"));
    }

    [Fact]
    public void LoadFromBytes_NamespaceFreeGraphMl_StillParses()
    {
        // Tolerant of a minimal export without the standard xmlns.
        const string noNamespace = """
        <graphml>
          <graph id="G" edgedefault="directed">
            <node id="A100"><data key="label">Account</data></node>
            <node id="A200"><data key="label">Account</data></node>
            <edge id="R-1" source="A100" target="A200"><data key="type">transferred_to</data></edge>
          </graph>
        </graphml>
        """;
        var adapter = new GraphMlDataAdapter();
        var dataset = adapter.LoadFromBytes(Bytes(noNamespace), "minimal.graphml");
        Assert.Equal(2, dataset.Entities.Count);
        Assert.Single(dataset.Relationships);
    }

    [Fact]
    public async Task LoadAsync_FileNotFound_ThrowsAdapterSourceException()
    {
        var adapter = new GraphMlDataAdapter();
        var source = new DataSourceConfiguration("graphml", Path: "does/not/exist.graphml");
        await Assert.ThrowsAsync<AdapterSourceException>(() => adapter.LoadAsync(source));
    }
}
