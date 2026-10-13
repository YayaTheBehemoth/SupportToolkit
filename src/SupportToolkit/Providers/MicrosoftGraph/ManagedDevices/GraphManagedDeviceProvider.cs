
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;

/// <summary>
/// Read-only Microsoft Graph managed-device retrieval.
///
/// Authentication and HTTP transport are supplied externally.
/// This milestone runs through Dev Proxy without credentials.
/// </summary>
public sealed class GraphManagedDeviceProvider
    : IGraphManagedDeviceProvider
{
    private const int MaxPages = 100;

    private static readonly Uri InitialEndpoint = new(
        "https://graph.microsoft.com/v1.0/" +
        "deviceManagement/managedDevices?$top=5"
    );

    private readonly HttpClient _httpClient;

    public GraphManagedDeviceProvider(HttpClient httpClient)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<ManagedDevice>>
        GetManagedDevicesAsync(
            CancellationToken cancellationToken = default)
    {
        var devices = new List<ManagedDevice>();

        var visitedUrls = new HashSet<string>(
            StringComparer.Ordinal
        );

        Uri? nextPage = InitialEndpoint;

        while (nextPage is not null)
        {
            ValidateEndpoint(nextPage);

            if (visitedUrls.Count >= MaxPages)
            {
                throw new InvalidDataException(
                    "Microsoft Graph pagination exceeded " +
                    "the development safety limit."
                );
            }

            if (!visitedUrls.Add(nextPage.AbsoluteUri))
            {
                throw new InvalidDataException(
                    "Microsoft Graph returned a repeated " +
                    "pagination URL."
                );
            }

            using var response = await _httpClient.GetAsync(
                nextPage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );

            response.EnsureSuccessStatusCode();

            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken
                );

            var page = await JsonSerializer.DeserializeAsync<
                ManagedDevicePage
            >(
                stream,
                cancellationToken: cancellationToken
            );

            if (page?.Value is null)
            {
                throw new InvalidDataException(
                    "Microsoft Graph response is missing " +
                    "the managed-device value array."
                );
            }

            if (page.Value.Any(
                device => string.IsNullOrWhiteSpace(device.Id)))
            {
                throw new InvalidDataException(
                    "Microsoft Graph returned a managed " +
                    "device without an ID."
                );
            }

            devices.AddRange(page.Value);

            if (string.IsNullOrWhiteSpace(page.NextLink))
            {
                nextPage = null;
                continue;
            }

            if (!Uri.TryCreate(
                    page.NextLink,
                    UriKind.Absolute,
                    out var continuation))
            {
                throw new InvalidDataException(
                    "Microsoft Graph returned an invalid " +
                    "pagination URL."
                );
            }

            ValidateEndpoint(continuation);

            nextPage = continuation;
        }

        return devices.AsReadOnly();
    }

    private static void ValidateEndpoint(Uri uri)
    {
        var allowed =
            uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(
                uri.Host,
                "graph.microsoft.com",
                StringComparison.OrdinalIgnoreCase
            )
            && uri.Port == 443
            && string.Equals(
                uri.AbsolutePath,
                "/v1.0/deviceManagement/managedDevices",
                StringComparison.Ordinal
            )
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment);

        if (!allowed)
        {
            throw new InvalidOperationException(
                "Blocked unexpected Microsoft Graph " +
                "managed-device endpoint."
            );
        }
    }

    private sealed class ManagedDevicePage
    {
        [JsonPropertyName("value")]
        public List<ManagedDevice>? Value { get; init; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }
    }
}
