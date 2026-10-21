using SupportToolkit.Core.Logging;
using SupportToolkit.Providers.BusinessCentral.Auth;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Providers.BusinessCentral.Subscriptions;

/// <summary>
/// Reads SMARTsubscription headers and lines from the custom
/// Business Central API pages.
///
/// Read-only: exactly two page paths are approved, and every request
/// asks only for the DTO fields through $select.
/// </summary>
public sealed class BusinessCentralSubscriptionProvider
    : IBusinessCentralSubscriptionProvider
{
    private const string HeadersEntitySet =
        "smartSubscriptionHeaders";

    private const string LinesEntitySet =
        "smartSubscriptionLines";

    private readonly BusinessCentralODataPaginator _paginator;
    private readonly string _headersPath;
    private readonly string _linesPath;

    public BusinessCentralSubscriptionProvider(
        BusinessCentralOptions options,
        BusinessCentralODataPaginator paginator)
    {
        ArgumentNullException.ThrowIfNull(options);

        _paginator =
            paginator
            ?? throw new ArgumentNullException(nameof(paginator));

        _headersPath =
            $"{options.CompanyPath}/{HeadersEntitySet}";

        _linesPath =
            $"{options.CompanyPath}/{LinesEntitySet}";
    }

    /// <summary>
    /// The only paths this provider reads, for the transport
    /// allowlist.
    /// </summary>
    public static IReadOnlyList<string> ApprovedPaths(
        BusinessCentralOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return
        [
            $"{options.CompanyPath}/{HeadersEntitySet}",
            $"{options.CompanyPath}/{LinesEntitySet}"
        ];
    }

    /// <summary>
    /// Assembles the provider with its pinned read-only pipeline.
    /// </summary>
    public static BusinessCentralSubscriptionProvider Create(
        BusinessCentralOptions options,
        IBusinessCentralTokenProvider tokenProvider,
        OperationalLogger? logger = null,
        HttpMessageHandler? networkHandler = null)
    {
        var httpClient =
            BusinessCentralHttpPipeline.Create(
                options,
                tokenProvider,
                ApprovedPaths(options),
                networkHandler
            );

        var client =
            new BusinessCentralApiClient(
                httpClient,
                logger
            );

        return new BusinessCentralSubscriptionProvider(
            options,
            new BusinessCentralODataPaginator(
                client,
                logger
            )
        );
    }

    public async Task<BusinessCentralSubscriptionData>
        GetSubscriptionsAsync(
            CancellationToken cancellationToken = default)
    {
        var headers =
            await _paginator.FetchAllAsync<
                BusinessCentralSubscriptionHeaderDto?>(
                "subscription headers",
                $"{_headersPath}?$select=" +
                BusinessCentralSubscriptionFields.HeaderSelect,
                cancellationToken
            );

        var lines =
            await _paginator.FetchAllAsync<
                BusinessCentralSubscriptionLineDto?>(
                "subscription lines",
                $"{_linesPath}?$select=" +
                BusinessCentralSubscriptionFields.LineSelect,
                cancellationToken
            );

        return new BusinessCentralSubscriptionData(
            headers,
            lines
        );
    }
}
