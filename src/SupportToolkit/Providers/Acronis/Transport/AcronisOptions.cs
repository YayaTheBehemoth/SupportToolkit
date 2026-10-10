namespace SupportToolkit.Providers.Acronis.Transport;

/// <summary>
/// Runtime configuration required to authenticate against an Acronis
/// data center.
///
/// Resolution of configuration is handled outside the provider. This class
/// owns validation of the final Acronis-specific values.
/// </summary>
public sealed class AcronisOptions
{
    public required string DatacenterUrl { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public static AcronisOptions Create(
        string datacenterUrl,
        string clientId,
        string clientSecret)
    {
        datacenterUrl =
            RequireValue(
                datacenterUrl,
                "Acronis datacenter URL"
            );

        clientId =
            RequireValue(
                clientId,
                "Acronis client ID"
            );

        clientSecret =
            RequireValue(
                clientSecret,
                "Acronis client secret"
            );

        ValidateDatacenterUrl(
            datacenterUrl
        );

        return new AcronisOptions
        {
            DatacenterUrl =
                datacenterUrl,

            ClientId =
                clientId,

            ClientSecret =
                clientSecret
        };
    }

    public static AcronisOptions FromEnvironment(
        Func<string, string?>? environmentReader = null)
    {
        environmentReader ??=
            Environment.GetEnvironmentVariable;

        return Create(
            RequireEnvironmentVariable(
                environmentReader,
                "ACRONIS_DATACENTER_URL"
            ),
            RequireEnvironmentVariable(
                environmentReader,
                "ACRONIS_CLIENT_ID"
            ),
            RequireEnvironmentVariable(
                environmentReader,
                "ACRONIS_CLIENT_SECRET"
            )
        );
    }

    private static string RequireEnvironmentVariable(
        Func<string, string?> environmentReader,
        string variableName)
    {
        var value =
            environmentReader(
                variableName
            );

        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                $"Required environment variable " +
                $"'{variableName}' is not configured."
            );
        }

        return value.Trim();
    }

    private static string RequireValue(
        string value,
        string description)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                $"{description} is not configured."
            );
        }

        return value.Trim();
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
                "Acronis datacenter URL must be a valid absolute HTTPS URL."
            );
        }
    }
}