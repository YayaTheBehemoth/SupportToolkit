using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Core.Secrets;

/// <summary>
/// Generates stable logical identifiers for SupportToolkit secrets.
///
/// Secret identifiers are profile-scoped so multiple environments can coexist
/// without sharing credentials accidentally.
/// </summary>
public static class SupportToolkitSecretKeys
{
    public static string AcronisClientSecret(
        string profileName)
    {
        return Build(
            profileName,
            "acronis.client-secret"
        );
    }

    public static string ZendeskClientSecret(
        string profileName)
    {
        return Build(
            profileName,
            "zendesk.client-secret"
        );
    }

    private static string Build(
        string profileName,
        string secretName)
    {
        var normalizedProfileName =
            SupportToolkitProfileNames.Normalize(
                profileName
            );

        return
            $"{normalizedProfileName}:{secretName}";
    }
}