namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Defines which data source a SupportToolkit module should use at runtime.
///
/// Fixture mode is safe for local development and demonstrations.
/// Production mode allows the module to use its configured external provider.
/// </summary>
public enum SupportToolkitMode
{
    Fixture,
    Production
}

/// <summary>
/// Resolves runtime mode independently for each SupportToolkit module.
///
/// Every module defaults to fixture mode so enabling production behavior for
/// one module cannot implicitly enable it for another.
/// </summary>
public sealed class SupportToolkitRuntimeOptions
{
    public required SupportToolkitMode Mode { get; init; }

    public static SupportToolkitRuntimeOptions FromEnvironment(
        string moduleCommand,
        Func<string, string?>? environmentReader = null)
    {
        environmentReader ??=
            Environment.GetEnvironmentVariable;

        var variableName =
            GetEnvironmentVariableName(
                moduleCommand
            );

        var value =
            environmentReader(
                variableName
            );

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return new SupportToolkitRuntimeOptions
            {
                Mode =
                    SupportToolkitMode.Fixture
            };
        }

        if (value.Equals(
                "fixture",
                StringComparison.OrdinalIgnoreCase))
        {
            return new SupportToolkitRuntimeOptions
            {
                Mode =
                    SupportToolkitMode.Fixture
            };
        }

        if (value.Equals(
                "production",
                StringComparison.OrdinalIgnoreCase))
        {
            return new SupportToolkitRuntimeOptions
            {
                Mode =
                    SupportToolkitMode.Production
            };
        }

        throw new InvalidOperationException(
            $"{variableName} must be either " +
            "'fixture' or 'production'."
        );
    }

    public static string GetEnvironmentVariableName(
        string moduleCommand)
    {
        if (string.IsNullOrWhiteSpace(
                moduleCommand))
        {
            throw new ArgumentException(
                "Module command cannot be empty.",
                nameof(moduleCommand)
            );
        }

        var normalizedCommand =
            moduleCommand.Trim();

        if (normalizedCommand.Any(
                character =>
                    !char.IsLetterOrDigit(
                        character
                    )
                    && character != '-'
                    && character != '_'
            ))
        {
            throw new ArgumentException(
                "Module command may contain only letters, " +
                "numbers, hyphens, and underscores.",
                nameof(moduleCommand)
            );
        }

        normalizedCommand =
            normalizedCommand
                .Replace(
                    '-',
                    '_'
                )
                .ToUpperInvariant();

        return
            $"SUPPORTTOOLKIT_{normalizedCommand}_MODE";
    }
}