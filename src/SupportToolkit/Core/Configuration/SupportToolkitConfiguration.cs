
namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Persistent, non-secret SupportToolkit configuration.
///
/// Connection metadata is independent from runtime profiles so the
/// same connection may be used by more than one profile.
///
/// Authentication secrets belong in ISecretStore.
/// </summary>
public sealed class SupportToolkitConfiguration : ConfigurationNode
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
public sealed class SupportToolkitConnections : ConfigurationNode
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

    /// <summary>
    /// Zendesk connections used for interactive ticket deletion.
    ///
    /// Deliberately separate from Zendesk (ticket creation): a different
    /// OAuth client, a different sign-in flow, a different scope, and no
    /// shared credentials.
    /// </summary>
    public Dictionary<string, ZendeskDeletionConnectionConfiguration>
        ZendeskDeletion
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
public sealed class SupportToolkitProfile : ConfigurationNode
{
    public string? AcronisConnection { get; set; }

    public string? ZendeskConnection { get; set; }

    public string? ZendeskDestination { get; set; }

    /// <summary>
    /// The Zendesk connection used for interactive ticket deletion.
    /// Independent of ZendeskConnection, which is used to create tickets.
    /// </summary>
    public string? ZendeskDeletionConnection { get; set; }

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

    /// <summary>
    /// Per-module review settings, keyed by module command.
    /// </summary>
    public Dictionary<string, ModuleReviewConfiguration> Review
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
public sealed class AcronisConnectionConfiguration : ConfigurationNode
{
    public string DatacenterUrl { get; set; } =
        string.Empty;

    public string ClientId { get; set; } =
        string.Empty;
}

/// <summary>
/// How a module review treats resources that are still running.
/// Unset values use the defaults in InProgressWaitSettings. A maximum
/// wait of 0 disables waiting.
/// </summary>
public sealed class ModuleReviewConfiguration : ConfigurationNode
{
    public int? InProgressMaxWaitSeconds { get; set; }

    public int? InProgressPollIntervalSeconds { get; set; }
}

/// <summary>
/// Settings one module needs to use the shared monitoring scope.
/// </summary>
public sealed class ModuleMonitoringScopeConfiguration : ConfigurationNode
{
    /// <summary>
    /// Item number that identifies the module's monitoring service
    /// in subscription lines.
    /// </summary>
    public string MonitoringItemNumber { get; set; } =
        string.Empty;

    /// <summary>
    /// Item numbers that confer monitoring entitlement. When empty,
    /// the single MonitoringItemNumber applies. Neither being set is a
    /// configuration error; there is no implicit default.
    /// </summary>
    public List<string> MonitoringItemNumbers { get; set; } =
        [];

    /// <summary>
    /// Item numbers that show a customer has the underlying service
    /// this module watches (for backups: the backup items themselves),
    /// with or without the monitoring entitlement. A customer holding
    /// one is a known customer of the service, so its tenant can be
    /// matched and decided as not entitled instead of staying
    /// unresolved. The entitling items count as service items too.
    /// Optional; empty means only entitled customers are matched.
    /// </summary>
    public List<string> ServiceItemNumbers { get; set; } =
        [];

    /// <summary>
    /// Acronis tenant IDs that are never reconciled, such as the
    /// operator's own tenant.
    /// </summary>
    public List<string> ExcludedTenantIds { get; set; } =
        [];
}

/// <summary>
/// Non-secret Business Central connection metadata.
///
/// Delegated sign-in uses a public client, so no secret exists.
/// Hosts, authority and scope are pinned in code and are not
/// configurable. Values here are validated by the provider's options.
/// </summary>
public sealed class BusinessCentralConnectionConfiguration : ConfigurationNode
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
///
/// SubmitterId and RequesterId are the Zendesk user IDs created tickets
/// are attributed to. Both are required for ticket submission; null
/// means not configured. They are user IDs within this instance.
/// </summary>
public sealed class ZendeskConnectionConfiguration : ConfigurationNode
{
    public string Subdomain { get; set; } =
        string.Empty;

    public string ClientId { get; set; } =
        string.Empty;

    public long? SubmitterId { get; set; }

    public long? RequesterId { get; set; }

    public Dictionary<string, ZendeskDestinationConfiguration> Destinations
    {
        get;
        set;
    } = new();
}

/// <summary>
/// Non-secret metadata of a Zendesk connection used for interactive
/// ticket deletion.
///
/// The OAuth client is public and signs in with PKCE in the operator's
/// browser, so no client secret exists, is asked for, or is stored.
/// There are no destinations or user IDs: deletion acts as the signed-in
/// operator. This is a different connection from the ticketing one even
/// when both point at the same Zendesk subdomain.
/// </summary>
public sealed class ZendeskDeletionConnectionConfiguration : ConfigurationNode
{
    public string Subdomain { get; set; } =
        string.Empty;

    public string ClientId { get; set; } =
        string.Empty;
}

/// <summary>
/// A named ticket destination within one Zendesk instance.
///
/// GroupId is the Zendesk group_id used for ticket routing.
/// Zero is invalid and must be rejected during resolution.
/// </summary>
public sealed class ZendeskDestinationConfiguration : ConfigurationNode
{
    public long GroupId { get; set; }
}
