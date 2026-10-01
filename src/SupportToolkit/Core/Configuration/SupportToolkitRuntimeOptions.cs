namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Defines which external data source SupportToolkit should use at runtime.
/// Fixture mode is safe for local development and demonstrations, while
/// production mode uses real external providers.
/// </summary>
public enum SupportToolkitMode
{
    Fixture,
    Production
}

/// <summary>
/// Resolves the application runtime mode from environment configuration.
/// Fixture mode is the default so running the application locally can never
/// accidentally contact production systems.
/// </summary>
public sealed class SupportToolkitRuntimeOptions
{
    public required SupportToolkitMode Mode { get; init; }

    public static SupportToolkitRuntimeOptions FromEnvironment(
        Func<string, string?>? environmentReader = null)
    {
        environmentReader ??= Environment.GetEnvironmentVariable;

        var value = environmentReader(
            "SUPPORTTOOLKIT_MODE"
        );

        if (string.IsNullOrWhiteSpace(value))
        {
            return new SupportToolkitRuntimeOptions
            {
                Mode = SupportToolkitMode.Fixture
            };
        }

        if (value.Equals(
                "fixture",
                StringComparison.OrdinalIgnoreCase))
        {
            return new SupportToolkitRuntimeOptions
            {
                Mode = SupportToolkitMode.Fixture
            };
        }

        if (value.Equals(
                "production",
                StringComparison.OrdinalIgnoreCase))
        {
            return new SupportToolkitRuntimeOptions
            {
                Mode = SupportToolkitMode.Production
            };
        }

        throw new InvalidOperationException(
            "SUPPORTTOOLKIT_MODE must be either " +
            "'fixture' or 'production'."
        );
    }
}