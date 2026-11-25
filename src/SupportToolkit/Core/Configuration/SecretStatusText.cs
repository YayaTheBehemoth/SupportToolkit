using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// How a secret's presence is shown in status output. When the store reads
/// secrets from outside (an environment variable), the place is named so
/// the operator knows what to set. A value is never shown.
/// </summary>
public static class SecretStatusText
{
    public static string Describe(
        ISecretStore secrets,
        string key,
        bool present,
        string presentText,
        string missingText)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        var text = present ? presentText : missingText;
        var location = secrets.ExternalLocation(key);

        return location is null
            ? text
            : $"{text} ({location})";
    }
}
