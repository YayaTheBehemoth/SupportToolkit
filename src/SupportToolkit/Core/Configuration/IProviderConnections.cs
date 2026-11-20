namespace SupportToolkit.Core.Configuration;

/// <summary>
/// A provider that owns its connection settings: they live under
/// connections.&lt;section&gt; in the configuration file, and only the
/// provider knows their shape (it reads them with ProviderSettings). Core
/// knows only what every connection has: a name and its secrets.
///
/// Each implementation is registered once, in Composition/, so required
/// secrets, secret clashes and status cover it without Core naming it.
/// </summary>
public interface IProviderConnections
{
    /// <summary>The provider as operators know it ("osTicket").</summary>
    string ProviderName { get; }

    /// <summary>Every saved connection of this provider.</summary>
    IReadOnlyCollection<string> GetSavedConnectionNames(
        SupportToolkitConfiguration configuration);

    /// <summary>
    /// The saved connections at least one profile uses. Only these need
    /// their secrets on this computer.
    /// </summary>
    IReadOnlyCollection<string> GetConnectionsInUse(
        SupportToolkitConfiguration configuration);

    /// <summary>
    /// The secrets one saved connection needs, in display order: one for
    /// most providers, one per server for a provider whose servers each
    /// have their own.
    /// </summary>
    IReadOnlyList<ProviderSecret> GetSecrets(
        SupportToolkitConfiguration configuration,
        string connectionName);
}

/// <summary>
/// One secret a provider connection needs.
///
/// Key is its secret store key. Kind says what it is ("API key",
/// "password"). Owner is what it belongs to, for clash messages ("lab",
/// "lab, server machine-a"). Label names it in prompts and status ("API
/// key", "password of server 'machine-a'").
/// </summary>
public sealed record ProviderSecret(
    string Key,
    string Kind,
    string Owner,
    string Label);
