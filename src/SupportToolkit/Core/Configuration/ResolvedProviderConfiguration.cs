
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
/// Resolved Zendesk deletion connection: a public OAuth client.
///
/// There is no secret: sign-in is interactive (PKCE in the operator's
/// browser). Presence is checked during resolution; subdomain format
/// belongs to the provider's options. This never carries ticket
/// destination or user IDs, which belong to the ticketing connection.
/// </summary>
public sealed record ResolvedZendeskDeletionConnection(
    string ConnectionName,
    string Subdomain,
    string ClientId
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
