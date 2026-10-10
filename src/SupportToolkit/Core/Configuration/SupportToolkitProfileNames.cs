namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Defines stable profile naming rules used by both configuration and secret
/// storage.
///
/// Normalizing names prevents profile casing from creating multiple logical
/// secret namespaces.
/// </summary>
public static class SupportToolkitProfileNames
{
    public static string Normalize(
        string profileName)
    {
        if (string.IsNullOrWhiteSpace(
                profileName))
        {
            throw new ArgumentException(
                "Profile name cannot be empty.",
                nameof(profileName)
            );
        }

        var normalized =
            profileName
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
                "Profile name may contain only letters, numbers, " +
                "hyphens, underscores, and periods.",
                nameof(profileName)
            );
        }

        return normalized;
    }
}