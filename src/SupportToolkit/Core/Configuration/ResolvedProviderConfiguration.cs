namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Fully resolved Acronis connection settings ready for runtime use.
///
/// These values may have come from persistent configuration, environment
/// overrides, and the configured secret store.
/// </summary>
public sealed record ResolvedAcronisConnection(
    string DatacenterUrl,
    string ClientId,
    string ClientSecret
);

/// <summary>
/// Fully resolved Zendesk connection settings ready for runtime use.
///
/// These values may have come from persistent configuration, environment
/// overrides, and the configured secret store.
/// </summary>
public sealed record ResolvedZendeskConnection(
    string Subdomain,
    string ClientId,
    string ClientSecret
);