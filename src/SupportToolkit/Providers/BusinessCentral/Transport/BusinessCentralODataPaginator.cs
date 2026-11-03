using System.Text.Json.Serialization;
using SupportToolkit.Core.Logging;

namespace SupportToolkit.Providers.BusinessCentral.Transport;

/// <summary>
/// OData collection envelope returned by Business Central API pages.
/// </summary>
public sealed class ODataCollectionDto<TItem>
{
    [JsonPropertyName("value")]
    public List<TItem>? Value { get; init; }

    [JsonPropertyName("@odata.nextLink")]
    public string? NextLink { get; init; }
}

/// <summary>
/// Follows OData continuation links for one collection.
///
/// Fetching is all-or-nothing: any failure, malformed envelope or
/// unsafe continuation link throws, so a partial collection is never
/// returned as if it were complete.
///
/// Continuation links are server-supplied and are validated against
/// the configured API origin and base path before being followed.
/// </summary>
public sealed class BusinessCentralODataPaginator
{
    private const int MaxPages = 200;

    private const int MaxItems = 100_000;

    private readonly BusinessCentralApiClient _client;
    private readonly OperationalLogger? _logger;

    public BusinessCentralODataPaginator(
        BusinessCentralApiClient client,
        OperationalLogger? logger = null)
    {
        _client =
            client
            ?? throw new ArgumentNullException(nameof(client));

        _logger = logger;
    }

    public async Task<IReadOnlyList<TItem>> FetchAllAsync<TItem>(
        string datasetName,
        string firstRelativePath,
        CancellationToken cancellationToken = default)
    {
        var items =
            new List<TItem>();

        var seenLinks =
            new HashSet<string>(StringComparer.Ordinal);

        string? path =
            firstRelativePath;

        var pageNumber = 1;

        while (path is not null)
        {
            if (pageNumber > MaxPages)
            {
                throw new BusinessCentralContractException(
                    $"Business Central {datasetName} pagination " +
                    $"exceeded {MaxPages} pages."
                );
            }

            var page =
                await _client.GetJsonAsync<
                    ODataCollectionDto<TItem>>(
                    path,
                    cancellationToken
                );

            if (page.Value is null)
            {
                throw new BusinessCentralContractException(
                    $"Business Central {datasetName} response " +
                    "has no value collection."
                );
            }

            items.AddRange(page.Value);

            if (items.Count > MaxItems)
            {
                throw new BusinessCentralContractException(
                    $"Business Central {datasetName} exceeded " +
                    $"{MaxItems} items."
                );
            }

            _logger?.Debug(
                $"{datasetName} page {pageNumber}: " +
                $"{page.Value.Count} item(s)."
            );

            if (string.IsNullOrWhiteSpace(page.NextLink))
            {
                path = null;
            }
            else
            {
                if (!seenLinks.Add(page.NextLink))
                {
                    throw new BusinessCentralContractException(
                        $"Business Central {datasetName} " +
                        "pagination returned a repeated " +
                        "continuation link."
                    );
                }

                path =
                    ToRelativePath(
                        page.NextLink,
                        datasetName
                    );
            }

            pageNumber++;
        }

        return items.AsReadOnly();
    }

    private string ToRelativePath(
        string nextLink,
        string datasetName)
    {
        var baseUri =
            _client.ApiBaseUri;

        if (!Uri.TryCreate(
                nextLink,
                UriKind.Absolute,
                out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(
                uri.Host,
                baseUri.Host,
                StringComparison.OrdinalIgnoreCase)
            || uri.Port != baseUri.Port
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !uri.AbsolutePath.StartsWith(
                baseUri.AbsolutePath,
                StringComparison.Ordinal)
            || uri.AbsolutePath.Contains(
                "..",
                StringComparison.Ordinal))
        {
            throw new BusinessCentralContractException(
                $"Business Central {datasetName} returned a " +
                "continuation link outside the configured API."
            );
        }

        return uri.AbsolutePath[baseUri.AbsolutePath.Length..]
            + uri.Query;
    }
}
