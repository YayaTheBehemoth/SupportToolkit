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