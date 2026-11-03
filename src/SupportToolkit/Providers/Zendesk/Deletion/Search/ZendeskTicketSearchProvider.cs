using System.Globalization;
using SupportToolkit.Providers.Zendesk.Deletion.Search.Dtos;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Providers.Zendesk.Deletion.Search;

public sealed record ZendeskTicketSummary(
    long Id,
    string? Status,
    string? Subject,
    long? RequesterId,
    long? OrganizationId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// The tickets a search returned, with an explicit completeness verdict.
///
/// IsComplete is true only when Zendesk's reported count equals the number
/// of unique tickets received and within the search limit. Anything else
/// is incomplete and says why; an incomplete result must never be shown
/// or acted on as if it were the whole set.
/// </summary>
public sealed record ZendeskSearchResult(
    IReadOnlyList<ZendeskTicketSummary> Tickets,
    int? ReportedCount,
    int DuplicateCount,
    bool IsComplete,
    string? IncompleteReason);

/// <summary>
/// Read-only ticket search.
///
/// Zendesk search returns at most 1,000 results (100 per page, offset
/// paging) and can repeat a ticket across pages, so tickets are
/// de-duplicated and the reported count is checked against the unique
/// tickets received. A failed page fails the whole search: no partial
/// list is returned.
/// </summary>
public sealed class ZendeskTicketSearchProvider
{
    public const int PageSize = 100;

    public const int MaxResults = 1000;

    public const int MaxPages = MaxResults / PageSize;

    private readonly ZendeskDeletionApiClient _client;

    public ZendeskTicketSearchProvider(
        ZendeskDeletionApiClient client)
    {
        _client =
            client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ZendeskSearchResult> SearchTicketsAsync(
        ZendeskSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = ZendeskSearchQuery.Build(criteria);

        var tickets =
            new Dictionary<long, ZendeskTicketSummary>();

        var received = 0;
        var malformed = 0;
        int? reportedCount = null;
        var moreBeyondLimit = false;

        for (var page = 1; page <= MaxPages; page++)
        {
            var path =
                "/api/v2/search.json" +
                $"?query={Uri.EscapeDataString(query)}" +
                "&sort_by=created_at&sort_order=asc" +
                $"&per_page={PageSize}&page={page}";

            var dto =
                await _client.GetJsonAsync<ZendeskSearchPageDto>(
                    path,
                    cancellationToken);

            if (page == 1)
            {
                reportedCount = dto.Count;
            }

            foreach (var result in dto.Results)
            {
                received++;

                if (result.Id is null or <= 0
                    || (result.ResultType is not null
                        && !string.Equals(
                            result.ResultType,
                            "ticket",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    malformed++;
                    continue;
                }

                // A repeat keeps the first copy.
                tickets.TryAdd(
                    result.Id.Value,
                    new ZendeskTicketSummary(
                        result.Id.Value,
                        result.Status,
                        result.Subject,
                        result.RequesterId,
                        result.OrganizationId,
                        ParseDate(result.CreatedAt),
                        ParseDate(result.UpdatedAt)));
            }

            if (string.IsNullOrWhiteSpace(dto.NextPage))
            {
                break;
            }

            if (page == MaxPages)
            {
                moreBeyondLimit = true;
            }
        }

        var unique = tickets.Count;
        var duplicates = received - malformed - unique;

        string? reason = null;

        if (reportedCount is null)
        {
            reason = "Zendesk did not report how many tickets match.";
        }
        else if (reportedCount > MaxResults)
        {
            reason =
                $"Zendesk reports {reportedCount} matching tickets but a " +
                $"search returns at most {MaxResults}. Narrow the search.";
        }
        else if (moreBeyondLimit)
        {
            reason =
                $"Zendesk offered more than {MaxResults} results. " +
                "Narrow the search.";
        }
        else if (malformed > 0)
        {
            reason =
                $"{malformed} result(s) were not valid tickets.";
        }
        else if (reportedCount != unique)
        {
            reason =
                $"Zendesk reports {reportedCount} matching tickets but " +
                $"{unique} unique tickets were received.";
        }

        return new ZendeskSearchResult(
            tickets.Values
                .OrderBy(ticket => ticket.Id)
                .ToArray(),
            reportedCount,
            duplicates,
            reason is null,
            reason);
    }

    private static DateTimeOffset? ParseDate(
        string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
}
