namespace SupportToolkit.Core.Configuration;

/// <summary>
/// A provider that owns its connection settings: they live under
/// connections.&lt;section&gt; in the configuration file, and only the
/// provider knows their shape (it reads them with ProviderSettings). Core
/// knows only what every connection has: a name and one secret.
///
/// Each implementation is registered once, in Composition/, so required
/// secrets, secret clashes and status cover it without Core naming it.
/// </summary>
public interface IProviderConnections
{
    /// <summary>The provider as operators know it ("osTicket").</summary>
    string ProviderName { get; }

    /// <summary>What its secret is ("API key", "client secret").</summary>
    string SecretName { get; }

    /// <summary>Every saved connection of this provider.</summary>
    IReadOnlyCollection<string> GetSavedConnectionNames(
        SupportToolkitConfiguration configuration);

    /// <summary>
    /// The saved connections at least one profile uses. Only these need
    /// a secret on this computer.
    /// </summary>
    IReadOnlyCollection<string> GetConnectionsInUse(
        SupportToolkitConfiguration configuration);

    /// <summary>The secret store key of a connection's secret.</summary>
    string GetSecretKey(string connectionName);
}
