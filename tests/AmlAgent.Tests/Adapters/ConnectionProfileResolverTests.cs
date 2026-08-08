using AmlAgent.Adapters;
using AmlAgent.Adapters.Configuration;
using Xunit;

namespace AmlAgent.Tests.Adapters;

public class ConnectionProfileResolverTests
{
    [Fact]
    public void EnvVarNameFor_NormalisesHyphensAndCase()
    {
        Assert.Equal("AML_CONN_BANK_TEST", ConnectionProfileResolver.EnvVarNameFor("bank-test"));
    }

    [Fact]
    public void Resolve_MissingProfileName_ThrowsInvalidAdapterConfigurationException()
    {
        Assert.Throws<InvalidAdapterConfigurationException>(() => ConnectionProfileResolver.Resolve(null, "postgresql"));
        Assert.Throws<InvalidAdapterConfigurationException>(() => ConnectionProfileResolver.Resolve("  ", "postgresql"));
    }

    [Fact]
    public void Resolve_EnvVarNotSet_ThrowsInvalidAdapterConfigurationExceptionWithVarNameInMessage()
    {
        var profileName = $"test-profile-{Guid.NewGuid():N}";
        var ex = Assert.Throws<InvalidAdapterConfigurationException>(() => ConnectionProfileResolver.Resolve(profileName, "postgresql"));
        Assert.Contains("AML_CONN_", ex.Message);
    }

    [Fact]
    public void Resolve_EnvVarSet_ReturnsItsValue()
    {
        var profileName = $"test-profile-{Guid.NewGuid():N}";
        var envVar = ConnectionProfileResolver.EnvVarNameFor(profileName);
        Environment.SetEnvironmentVariable(envVar, "Host=localhost;Database=test");
        try
        {
            var resolved = ConnectionProfileResolver.Resolve(profileName, "postgresql");
            Assert.Equal("Host=localhost;Database=test", resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }
}
