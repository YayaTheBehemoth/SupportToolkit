using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Providers.Duplicati.Configuration;

/// <summary>
/// A Duplicati fleet, under connections.duplicati in the configuration
/// file: the customers an MSP backs up with Duplicati, and each
/// customer's servers. One Duplicati server backs up one machine and has
/// its own password, which lives in the secret store.
/// </summary>
public sealed class DuplicatiConnectionConfiguration : ConfigurationNode
{
    /// <summary>Customers by name, as operators know them.</summary>
    public Dictionary<string, DuplicatiCustomerConfiguration> Customers
    {
        get;
        set;
    } = new();
}

public sealed class DuplicatiCustomerConfiguration : ConfigurationNode
{
    /// <summary>The customer's servers, by a short alias.</summary>
    public Dictionary<string, DuplicatiServerConfiguration> Servers
    {
        get;
        set;
    } = new();
}

public sealed class DuplicatiServerConfiguration : ConfigurationNode
{
    /// <summary>The server's web address, such as https://backup.example.com/.</summary>
    public string Url { get; set; } =
        string.Empty;
}

/// <summary>
/// A Duplicati fleet validated locally, with every server's password.
/// </summary>
public sealed record ResolvedDuplicatiFleet(
    string ConnectionName,
    IReadOnlyList<ResolvedDuplicatiCustomer> Customers);

public sealed record ResolvedDuplicatiCustomer(
    string Name,
    IReadOnlyList<ResolvedDuplicatiServer> Servers);

public sealed record ResolvedDuplicatiServer(
    string Alias,
    Uri BaseUri,
    string Password)
{
    /// <summary>Never shows the password.</summary>
    public override string ToString() => $"{Alias} ({BaseUri})";
}
