
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

/// <summary>
/// Retrieves setting summaries and their per-device states.
///
/// Read-only transport. No authentication is implemented.
/// Designed for controlled Dev Proxy experiments.
/// </summary>
public sealed class GraphComplianceSettingProvider
    : IGraphComplianceSettingProvider
{
    private const string RootPath =
        "/v1.0/deviceManagement/" +
        "deviceCompliancePolicySettingStateSummaries";

    private const int MaxPages = 50;
    private const int MaxSummaries = 30;

    private static readonly Uri SummaryEndpoint = new(
        "https://graph.microsoft.com" + RootPath
    );

    private readonly HttpClient _httpClient;

    public GraphComplianceSettingProvider(
        HttpClient httpClient)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(
                nameof(httpClient)
            );
    }

    public async Task<IReadOnlyList<ComplianceSettingEvidence>>
        GetSettingEvidenceAsync(
            CancellationToken cancellationToken = default)
    {
        var summaries = await ReadPagesAsync<SummaryDto>(
            SummaryEndpoint,
            cancellationToken
        );

        if (summaries.Count > MaxSummaries)
        {
            throw new InvalidDataException(
                "Too many compliance setting summaries " +
                "for this bounded discovery workflow."
            );
        }

        var evidence = new List<ComplianceSettingEvidence>();

        foreach (var summary in summaries)
        {
            if (string.IsNullOrWhiteSpace(summary.Id))
            {
                throw new InvalidDataException(
                    "Compliance setting summary has no ID."
                );
            }

            var path =
                RootPath + "/" +
                Uri.EscapeDataString(summary.Id) +
                "/deviceComplianceSettingStates";

            var endpoint = new Uri(
                "https://graph.microsoft.com" + path
            );

            var states = await ReadPagesAsync<SettingStateDto>(
                endpoint,
                cancellationToken
            );

            foreach (var state in states)
            {
                evidence.Add(
                    new ComplianceSettingEvidence(
                        summary.Id,
                        state.SettingName
                            ?? summary.SettingName
                            ?? "Unnamed setting",
                        state.DeviceId,
                        state.DeviceName,
                        state.State
                    )
                );
            }
        }

        return evidence.AsReadOnly();
    }

    private async Task<List<T>> ReadPagesAsync<T>(
        Uri initialEndpoint,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();

        var visited = new HashSet<string>(
            StringComparer.Ordinal
        );

        Uri? nextPage = initialEndpoint;

        while (nextPage is not null)
        {
            ValidateEndpoint(nextPage);

            if (visited.Count >= MaxPages)
            {
                throw new InvalidDataException(
                    "Compliance setting pagination " +
                    "exceeded the safety limit."
                );
            }

            if (!visited.Add(nextPage.AbsoluteUri))
            {
                throw new InvalidDataException(
                    "Compliance setting pagination repeated a URL."
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
                GraphPage<T>
            >(
                stream,
                cancellationToken: cancellationToken
            );

            if (page?.Value is null)
            {
                throw new InvalidDataException(
                    "Compliance setting response is " +
                    "missing its value array."
                );
            }

            items.AddRange(page.Value);

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
                    "Invalid compliance pagination URL."
                );
            }

            ValidateEndpoint(continuation);
            nextPage = continuation;
        }

        return items;
    }

    private static void ValidateEndpoint(Uri uri)
    {
        var validOrigin =
            uri.IsAbsoluteUri
            && uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(
                uri.Host,
                "graph.microsoft.com",
                StringComparison.OrdinalIgnoreCase
            )
            && uri.Port == 443
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment);

        if (!validOrigin)
        {
            throw new InvalidOperationException(
                "Blocked unexpected Graph origin."
            );
        }

        if (uri.AbsolutePath == RootPath)
        {
            return;
        }

        if (!uri.AbsolutePath.StartsWith(
            RootPath + "/",
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Blocked unexpected compliance endpoint."
            );
        }

        var remainder = uri.AbsolutePath[
            (RootPath.Length + 1)..
        ];

        var segments = remainder.Split('/');

        if (segments.Length != 2
            || string.IsNullOrWhiteSpace(segments[0])
            || segments[1] != "deviceComplianceSettingStates")
        {
            throw new InvalidOperationException(
                "Blocked unexpected compliance endpoint."
            );
        }
    }

    private sealed class GraphPage<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; init; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }
    }

    private sealed class SummaryDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("settingName")]
        public string? SettingName { get; init; }
    }

    private sealed class SettingStateDto
    {
        [JsonPropertyName("settingName")]
        public string? SettingName { get; init; }

        [JsonPropertyName("deviceId")]
        public string? DeviceId { get; init; }

        [JsonPropertyName("deviceName")]
        public string? DeviceName { get; init; }

        [JsonPropertyName("state")]
        public string? State { get; init; }
    }
}
