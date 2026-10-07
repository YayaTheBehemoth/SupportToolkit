namespace SupportToolkit.Providers.Acronis;

/// <summary>
/// Runtime configuration required to authenticate against an Acronis
/// data center. Secrets must be supplied at runtime and never committed
/// to source control.
/// </summary>
public sealed class AcronisOptions
{
    public required string DatacenterUrl { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public static AcronisOptions FromEnvironment(
        Func<string, string?>? environmentReader = null)
    {
        environmentReader ??= Environment.GetEnvironmentVariable;

        var datacenterUrl = Require(
            environmentReader,
            "ACRONIS_DATACENTER_URL"
        );

        ValidateDatacenterUrl(datacenterUrl);

        return new AcronisOptions
        {
            DatacenterUrl = datacenterUrl,

            ClientId = Require(
                environmentReader,
                "ACRONIS_CLIENT_ID"
            ),

            ClientSecret = Require(
                environmentReader,
                "ACRONIS_CLIENT_SECRET"
            )
        };
    }

    private static string Require(
        Func<string, string?> environmentReader,
        string variableName)
    {
        var value = environmentReader(variableName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required environment variable " +
                $"'{variableName}' is not configured."
            );
        }

        return value;
    }

    private static void ValidateDatacenterUrl(
        string datacenterUrl)
    {
        if (!Uri.TryCreate(
                datacenterUrl,
                UriKind.Absolute,
                out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "ACRONIS_DATACENTER_URL must be a valid absolute HTTPS URL."
            );
        }
    }
}