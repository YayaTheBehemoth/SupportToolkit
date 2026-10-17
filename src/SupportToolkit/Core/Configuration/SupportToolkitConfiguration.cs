
namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Persistent, non-secret SupportToolkit configuration.
///
/// Connection metadata is independent from runtime profiles so the
/// same connection may be used by more than one profile.
///
/// Authentication secrets belong in ISecretStore.
/// </summary>
public sealed class SupportToolkitConfiguration
{
    public string ActiveProfile { get; set; } = "local-dev";

    public SupportToolkitConnections Connections { get; set; } =
        new();

    public Dictionary<string, SupportToolkitProfile> Profiles
    {
        get;
        set;
    } = new();
}

/// <summary>
/// Named external connections available to profiles.
/// </summary>
public sealed class SupportToolkitConnections
{
    public Dictionary<string, AcronisConnectionConfiguration> Acronis
    {
        get;
        set;
    } = new();

    public Dictionary<string, ZendeskConnectionConfiguration> Zendesk
    {
        get;
        set;
    } = new();
}

/// <summary>
/// Defines one runtime profile.
///
/// Module modes control operational data sources.
/// Connection references select reusable external connections.
///
/// ZendeskDestination selects an explicitly configured group within
/// the profile's Zendesk connection.
/// </summary>
public sealed class SupportToolkitProfile
{
    public string? AcronisConnection { get; set; }

    public string? ZendeskConnection { get; set; }

    public string? ZendeskDestination { get; set; }

    public Dictionary<string, SupportToolkitMode> Modules
    {
        get;
        set;
    } = new();
}

/// <summary>
/// Non-secret Acronis connection metadata.
/// </summary>
public sealed class AcronisConnectionConfiguration
{
    public string DatacenterUrl { get; set; } =
        string.Empty;

    public string ClientId { get; set; } =
        string.Empty;
}

/// <summary>
/// Non-secret Zendesk connection metadata.
///
/// Destinations are scoped to this Zendesk instance, preventing
/// profiles from accidentally sharing group IDs across instances.
/// </summary>
public sealed class ZendeskConnectionConfiguration
{
    public string Subdomain { get; set; } =
        string.Empty;

    public string ClientId { get; set; } =
        string.Empty;

    public Dictionary<string, ZendeskDestinationConfiguration> Destinations
    {
        get;
        set;
    } = new();
}

/// <summary>
/// A named ticket destination within one Zendesk instance.
///
/// GroupId is the Zendesk group_id used for ticket routing.
/// Zero is invalid and must be rejected during resolution.
/// </summary>
public sealed class ZendeskDestinationConfiguration
{
    public long GroupId { get; set; }
}
