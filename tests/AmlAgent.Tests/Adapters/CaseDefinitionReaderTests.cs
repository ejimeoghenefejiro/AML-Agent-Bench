using AmlAgent.Adapters;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class CaseDefinitionReaderTests
{
    private const string Valid = """
    {
      "case_id": "case-001",
      "sources": [
        { "source_type": "csv", "path": "transactions.csv", "role": "transactions" },
        { "source_type": "json", "path": "customers.json", "role": "customers" }
      ]
    }
    """;

    [Fact]
    public void Parse_ValidDefinition_ParsesCaseIdAndSources()
    {
        var def = CaseDefinitionReader.Parse(Valid);
        Assert.Equal("case-001", def.CaseId);
        Assert.Equal(2, def.Sources.Count);
        Assert.Equal("csv", def.Sources[0].SourceType);
        Assert.Equal("transactions", def.Sources[0].Role);
        Assert.Equal("transactions.csv", def.Sources[0].Path);
    }

    [Fact]
    public void Parse_SourceOrderIsPreserved()
    {
        var def = CaseDefinitionReader.Parse(Valid);
        Assert.Equal("transactions", def.Sources[0].Role);
        Assert.Equal("customers", def.Sources[1].Role);
    }

    [Fact]
    public void Parse_DbSourceWithConnectionProfileAndQuery_Parsed()
    {
        const string json = """
        { "case_id": "c1", "sources": [
          { "source_type": "postgresql", "connection_profile": "bank-db", "query": "SELECT * FROM x", "role": "transactions" }
        ]}
        """;
        var def = CaseDefinitionReader.Parse(json);
        Assert.Equal("bank-db", def.Sources[0].ConnectionProfile);
        Assert.Equal("SELECT * FROM x", def.Sources[0].Query);
    }

    [Fact]
    public void Parse_SourceWithOptions_ParsedAsDictionary()
    {
        const string json = """
        { "case_id": "c1", "sources": [
          { "source_type": "rest", "connection_profile": "api", "options": { "AuthTokenProfile": "api-token" } }
        ]}
        """;
        var def = CaseDefinitionReader.Parse(json);
        Assert.Equal("api-token", def.Sources[0].Options!["AuthTokenProfile"]);
    }

    [Fact]
    public void Parse_MalformedJson_ThrowsInvalidCaseDefinitionException()
    {
        Assert.Throws<InvalidCaseDefinitionException>(() => CaseDefinitionReader.Parse("{not valid"));
    }

    [Fact]
    public void Parse_MissingCaseId_ThrowsInvalidCaseDefinitionException()
    {
        const string json = """{ "sources": [{ "source_type": "csv", "path": "x.csv" }] }""";
        var ex = Assert.Throws<InvalidCaseDefinitionException>(() => CaseDefinitionReader.Parse(json));
        Assert.Contains("case_id", ex.Message);
    }

    [Fact]
    public void Parse_MissingSources_ThrowsInvalidCaseDefinitionException()
    {
        const string json = """{ "case_id": "c1" }""";
        var ex = Assert.Throws<InvalidCaseDefinitionException>(() => CaseDefinitionReader.Parse(json));
        Assert.Contains("sources", ex.Message);
    }

    [Fact]
    public void Parse_EmptySourcesArray_ThrowsInvalidCaseDefinitionException()
    {
        const string json = """{ "case_id": "c1", "sources": [] }""";
        Assert.Throws<InvalidCaseDefinitionException>(() => CaseDefinitionReader.Parse(json));
    }

    [Fact]
    public void Parse_SourceMissingSourceType_ThrowsInvalidCaseDefinitionException()
    {
        const string json = """{ "case_id": "c1", "sources": [{ "path": "x.csv" }] }""";
        var ex = Assert.Throws<InvalidCaseDefinitionException>(() => CaseDefinitionReader.Parse(json));
        Assert.Contains("source_type", ex.Message);
    }

    [Fact]
    public void Parse_TopLevelArray_ThrowsInvalidCaseDefinitionException()
    {
        Assert.Throws<InvalidCaseDefinitionException>(() => CaseDefinitionReader.Parse("[1,2,3]"));
    }

    [Fact]
    public void ToDataSourceConfiguration_MapsFieldsCorrectly()
    {
        var def = CaseDefinitionReader.Parse(Valid);
        var config = def.Sources[0].ToDataSourceConfiguration();
        Assert.Equal("csv", config.SourceType);
        Assert.Equal("transactions.csv", config.Path);
    }
}
