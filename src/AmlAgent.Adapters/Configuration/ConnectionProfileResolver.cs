namespace AmlAgent.Adapters.Configuration;

/// <summary>
/// Resolves a named connection profile to an actual connection string/URI
/// via environment variables only -- never from a committed task or JSON
/// file (CLI-Only spec section 16/20). A profile named "bank-test" resolves
/// to the environment variable AML_CONN_BANK_TEST. Callers must never log
/// or write the resolved value anywhere (console, manifest, assurance
/// output) -- only the profile *name* is safe to record.
/// </summary>
public static class ConnectionProfileResolver
{
    public const string EnvVarPrefix = "AML_CONN_";

    public static string EnvVarNameFor(string profileName) =>
        EnvVarPrefix + Sanitise(profileName);

    /// <summary>Throws InvalidAdapterConfigurationException if the profile name is missing or its environment variable isn't set -- never returns a default/blank connection string.</summary>
    public static string Resolve(string? profileName, string adapterId)
    {
        if (string.IsNullOrWhiteSpace(profileName))
            throw new InvalidAdapterConfigurationException(adapterId, "'ConnectionProfile' is required for this adapter");

        var envVar = EnvVarNameFor(profileName);
        var value = Environment.GetEnvironmentVariable(envVar);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidAdapterConfigurationException(adapterId,
                $"connection profile '{profileName}' not found -- set environment variable {envVar} to the connection string/URI. " +
                "Connection details are never read from task files or committed JSON.");

        return value;
    }

    private static string Sanitise(string profileName) =>
        new string(profileName.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
