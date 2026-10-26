
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
