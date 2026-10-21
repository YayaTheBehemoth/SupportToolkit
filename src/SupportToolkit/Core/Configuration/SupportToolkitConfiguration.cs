
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

    public Dictionary<string, BusinessCentralConnectionConfiguration>
        BusinessCentral
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

    public string? BusinessCentralConnection { get; set; }

    /// <summary>
    /// Per-module monitoring-scope settings, keyed by module command.
    /// </summary>
    public Dictionary<string, ModuleMonitoringScopeConfiguration>
        MonitoringScope
    {
        get;
        set;
    } = new();

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
/// Settings one module needs to use the shared monitoring scope.
/// </summary>
public sealed class ModuleMonitoringScopeConfiguration
{
    /// <summary>
    /// Item number that identifies the module's monitoring service
    /// in subscription lines.
    /// </summary>
    public string MonitoringItemNumber { get; set; } =
        string.Empty;
}

/// <summary>
/// Non-secret Business Central connection metadata.
///
/// Delegated sign-in uses a public client, so no secret exists.
/// Hosts, authority and scope are pinned in code and are not
/// configurable. Values here are validated by the provider's options.
/// </summary>
public sealed class BusinessCentralConnectionConfiguration
{
    public string TenantId { get; set; } =
        string.Empty;

    public string ClientId { get; set; } =
        string.Empty;

    public string Environment { get; set; } =
        string.Empty;

    public string CompanyId { get; set; } =
        string.Empty;

    /// <summary>
    /// Optional. Used only to validate company names reported in
    /// rows, when a page reports them.
    /// </summary>
    public string? CompanyName { get; set; }

    public string ApiPublisher { get; set; } =
        string.Empty;

    public string ApiGroup { get; set; } =
        string.Empty;

    public string ApiVersion { get; set; } =
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
