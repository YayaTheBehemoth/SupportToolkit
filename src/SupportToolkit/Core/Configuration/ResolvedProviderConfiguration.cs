
namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Fully resolved Acronis connection settings ready for runtime use.
/// </summary>
public sealed record ResolvedAcronisConnection(
    string DatacenterUrl,
    string ClientId,
    string ClientSecret
);

/// <summary>
/// Resolved Business Central connection metadata.
///
/// Presence is checked during resolution. Format validation and the
/// pinned hosts belong to the Business Central provider options.
/// There is no secret: sign-in is delegated and interactive.
/// </summary>
public sealed record ResolvedBusinessCentralConnection(
    string TenantId,
    string ClientId,
    string Environment,
    string CompanyId,
    string? CompanyName,
    string ApiPublisher,
    string ApiGroup,
    string ApiVersion
);

/// <summary>
/// Fully resolved Zendesk connection and ticket destination.
///
/// DestinationName and GroupId are resolved from the active profile
/// and its named Zendesk connection. SubmitterId and RequesterId come
/// from the connection.
///
/// They must be valid before any Zendesk API requests are attempted.
/// </summary>
public sealed record ResolvedZendeskConnection(
    string Subdomain,
    string ClientId,
    string ClientSecret,
    string DestinationName,
    long GroupId,
    long SubmitterId,
    long RequesterId
);

/// <summary>
/// Acknowledged exceptions that could be interpreted, and a notice for
/// each configured entry that could not and was therefore not applied.
/// </summary>
public sealed record AcknowledgementSettings(
    IReadOnlyList<SupportToolkit.Core.Reconciliation.ExceptionAcknowledgement>
        Acknowledgements,
    IReadOnlyList<string> Notices
);
