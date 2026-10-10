namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Controls whether SupportToolkit may perform write operations against
/// external systems.
///
/// Writes are disabled by default and must be enabled explicitly for the
/// current runtime environment.
/// </summary>
public sealed class SupportToolkitWriteOptions
{
    public bool AllowWrites { get; init; }

    public static SupportToolkitWriteOptions FromEnvironment(
        Func<string, string?>? environmentReader = null)
    {
        environmentReader ??=
            Environment.GetEnvironmentVariable;

        var value =
            environmentReader(
                "SUPPORTTOOLKIT_ALLOW_WRITES"
            );

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return new SupportToolkitWriteOptions
            {
                AllowWrites =
                    false
            };
        }

        if (bool.TryParse(
                value,
                out var allowWrites))
        {
            return new SupportToolkitWriteOptions
            {
                AllowWrites =
                    allowWrites
            };
        }

        throw new InvalidOperationException(
            "SUPPORTTOOLKIT_ALLOW_WRITES must be either " +
            "'true' or 'false'."
        );
    }
}