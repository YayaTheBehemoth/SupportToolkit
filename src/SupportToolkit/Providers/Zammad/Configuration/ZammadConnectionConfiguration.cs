using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Providers.Zammad.Configuration;

/// <summary>
/// Non-secret Zammad connection settings, under connections.zammad in the
/// configuration file.
///
/// BaseUrl is the Zammad root (the folder holding api/). Customer is the
/// email address tickets are opened for; Zammad creates that customer on
/// first use. The API token is a secret and lives in the secret store.
///
/// BulkDelete allows bulk delete through this connection. It then needs a
/// second secret, the deletion token: a separate Zammad token with the
/// admin permission, so the token that creates tickets never can delete.
/// </summary>
public sealed class ZammadConnectionConfiguration : ConfigurationNode
{
    public string BaseUrl { get; set; } =
        string.Empty;

    public string Customer { get; set; } =
        string.Empty;

    public bool BulkDelete { get; set; }

    public Dictionary<string, ZammadDestinationConfiguration> Destinations
    {
        get;
        set;
    } = new();
}

/// <summary>
/// A named ticket destination within one Zammad instance: a group, by
/// name. Zammad refuses an unknown group before creating anything, so the
/// name is confirmed when a ticket is sent.
/// </summary>
public sealed class ZammadDestinationConfiguration : ConfigurationNode
{
    public string Group { get; set; } =
        string.Empty;
}

/// <summary>
/// A Zammad connection and destination, validated locally before any
/// Zammad request.
/// </summary>
public sealed record ResolvedZammadConnection(
    string ConnectionName,
    string BaseUrl,
    string ApiToken,
    string Customer,
    string DestinationName,
    string Group);

/// <summary>
/// A Zammad connection set up for bulk delete, with its deletion token,
/// validated locally before any Zammad request.
/// </summary>
public sealed record ResolvedZammadDeletionConnection(
    string ConnectionName,
    string BaseUrl,
    string DeletionToken);
