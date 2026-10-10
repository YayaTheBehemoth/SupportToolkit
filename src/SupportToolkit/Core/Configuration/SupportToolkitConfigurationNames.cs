namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Defines stable naming rules for persistent SupportToolkit configuration.
///
/// Profile and connection names share the same conservative character set so
/// they remain safe for JSON keys, command-line input, and secret identifiers.
/// </summary>
public static class SupportToolkitConfigurationNames
{
    public static string NormalizeProfileName(
        string profileName)
    {
        return Normalize(
            profileName,
            "Profile",
            nameof(profileName)
        );
    }

    public static string NormalizeConnectionName(
        string connectionName)
    {
        return Normalize(
            connectionName,
            "Connection",
            nameof(connectionName)
        );
    }

    private static string Normalize(
        string value,
        string description,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                $"{description} name cannot be empty.",
                parameterName
            );
        }

        var normalized =
            value
                .Trim()
                .ToLowerInvariant();

        if (normalized.Any(
                character =>
                    !char.IsLetterOrDigit(
                        character
                    )
                    && character != '-'
                    && character != '_'
                    && character != '.'
            ))
        {
            throw new ArgumentException(
                $"{description} name may contain only letters, numbers, " +
                "hyphens, underscores, and periods.",
                parameterName
            );
        }

        return normalized;
    }
}