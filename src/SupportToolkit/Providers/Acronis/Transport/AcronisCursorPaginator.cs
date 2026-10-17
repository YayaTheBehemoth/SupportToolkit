
using System.Net.Http.Json;
using SupportToolkit.Core.Logging;

namespace SupportToolkit.Providers.Acronis.Transport;

/// <summary>
/// Shared cursor-pagination helper for Acronis collection endpoints.
///
/// Endpoint-specific providers remain responsible for request paths
/// and response contracts.
/// </summary>
public sealed class AcronisCursorPaginator
{
    /*
     * A bounded traversal prevents an API that continuously returns
     * fresh cursors from generating unlimited requests.
     *
     * Current callers request 100 items per page. The limit permits
     * up to 500 pages before treating pagination as incomplete.
     */
    private const int MaxPages = 500;

    private readonly AcronisApiClient _apiClient;
    private readonly OperationalLogger? _logger;

    public AcronisCursorPaginator(
        AcronisApiClient apiClient,
        OperationalLogger? logger = null)
    {
        _apiClient =
            apiClient ?? throw new ArgumentNullException(nameof(apiClient));

        _logger = logger;
    }

    public async Task<IReadOnlyList<TItem>> FetchAllAsync<TPage, TItem>(
        string datasetName,
        string firstRequestPath,
        Func<string, string> buildNextRequestPath,
        Func<TPage, IEnumerable<TItem>> getItems,
        Func<TPage, string?> getAfterCursor,
        CancellationToken cancellationToken = default,
        string? accessToken = null)
        where TPage : class
    {
        var allItems =
            new List<TItem>();

        /*
         * Cursor tokens are opaque and case-sensitive.
         *
         * A repeated token means pagination is no longer advancing.
         * Detect both immediate repetition and longer cycles.
         *
         * Never include actual tokens in logs or exception messages.
         */
        var seenAfterCursors =
            new HashSet<string>(StringComparer.Ordinal);

        string? requestPath =
            firstRequestPath;

        var pageNumber = 1;

        while (requestPath is not null)
        {
            if (pageNumber > MaxPages)
            {
                throw new InvalidOperationException(
                    $"Acronis {datasetName} pagination exceeded " +
                    $"{MaxPages} pages."
                );
            }

            _logger?.Debug(
                $"Fetching {datasetName} page {pageNumber}."
            );

            var page =
                await GetPageAsync<TPage>(
                    requestPath,
                    cancellationToken,
                    accessToken
                );

            var pageItems =
                getItems(page)
                    .ToList();

            allItems.AddRange(pageItems);

            _logger?.Debug(
                $"{datasetName} page {pageNumber}: " +
                $"{pageItems.Count} item(s)."
            );

            var after =
                getAfterCursor(page);

            if (string.IsNullOrWhiteSpace(after))
            {
                requestPath = null;
            }
            else
            {
                if (!seenAfterCursors.Add(after))
                {
                    throw new InvalidOperationException(
                        $"Acronis {datasetName} pagination returned " +
                        "a repeated continuation cursor."
                    );
                }

                requestPath =
                    buildNextRequestPath(after);
            }

            pageNumber++;
        }

        _logger?.Debug(
            $"Completed {datasetName} pagination: " +
            $"{pageNumber - 1} page(s), {allItems.Count} item(s)."
        );

        return allItems.AsReadOnly();
    }

    private async Task<TPage> GetPageAsync<TPage>(
        string requestPath,
        CancellationToken cancellationToken,
        string? accessToken)
        where TPage : class
    {
        using var response =
            accessToken is null
                ? await _apiClient.GetAsync(
                    requestPath,
                    cancellationToken
                )
                : await _apiClient.GetWithAccessTokenAsync(
                    requestPath,
                    accessToken,
                    cancellationToken
                );

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<TPage>(
                cancellationToken: cancellationToken
            )
            ?? throw new InvalidOperationException(
                "Acronis returned an empty API response."
            );
    }
}
