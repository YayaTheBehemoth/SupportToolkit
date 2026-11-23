using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Core.Secrets;

/// <summary>
/// Generates stable logical identifiers for SupportToolkit secrets.
///
/// Secrets belong to named external connections rather than runtime profiles.
/// This allows many profiles to reuse one credential without duplicating the
/// stored secret.
/// </summary>
public static class SupportToolkitSecretKeys
{
    public static string AcronisClientSecret(
        string connectionName)
    {
        return Build(
            "acronis",
            connectionName,
            "client-secret"
        );
    }

    public static string ZendeskClientSecret(
        string connectionName)
    {
        return Build(
            "zendesk",
            connectionName,
            "client-secret"
        );
    }

    /// <summary>
    /// The key of a secret of a provider that owns its settings
    /// (IProviderConnections): provider:connection:secret, with the
    /// connection name normalized like every other.
    /// </summary>
    public static string For(
        string provider,
        string connectionName,
        string secretName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        return Build(
            provider.Trim().ToLowerInvariant(),
            connectionName,
            secretName.Trim().ToLowerInvariant()
        );
    }

    private static string Build(
        string provider,
        string connectionName,
        string secretName)
    {
        var normalizedConnectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    connectionName
                );

        return
            $"{provider}:{normalizedConnectionName}:{secretName}";
    }
}