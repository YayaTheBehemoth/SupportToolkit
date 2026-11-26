using System.Globalization;
using System.Net;
using System.Text.Json;
using SupportToolkit.Providers.Zammad.Deletion.Dtos;
using SupportToolkit.Providers.Zammad.Transport;

namespace SupportToolkit.Providers.Zammad.Deletion;

/// <summary>One ticket a Zammad search found, in Zammad's terms.</summary>
public sealed record ZammadTicket(
    long Id,
    string? Number,
    string? Title,
    string? State,
    long? OrganizationId,
    long? CustomerId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// The tickets a search found, with an explicit completeness verdict, and
/// the names and addresses Zammad sent with them.
/// </summary>
public sealed record ZammadSearchResult(
    IReadOnlyList<ZammadTicket> Tickets,
    int? ReportedCount,
    int DuplicateCount,
    bool IsComplete,
    string? IncompleteReason,
    IReadOnlyDictionary<long, string> OrganizationNames,
    IReadOnlyDictionary<long, string> CustomerEmails);

/// <summary>The user a token belongs to.</summary>
public sealed record ZammadUser(
    long Id,
    string? Name,
    string? Email,
    string? Roles);

public enum ZammadDeleteStatus
{
    /// <summary>HTTP 200: Zammad deleted the ticket, permanently.</summary>
    Deleted,

    /// <summary>HTTP 404: no such ticket (already deleted, or never existed).</summary>
    NotFound,

    /// <summary>HTTP 401: the token was refused; nothing was deleted.</summary>
    TokenRefused,

    /// <summary>
    /// Another 4xx (403 without the admin permission, 422): refused before
    /// anything was deleted.
    /// </summary>
    Refused,

    /// <summary>Anything else: whether the ticket was deleted is not known.</summary>
    Unclear
}

/// <summary>What Zammad answered to one deletion, with its own text.</summary>
public sealed record ZammadDeleteOutcome(
    ZammadDeleteStatus Status,
    int HttpStatus,
    string Detail);

/// <summary>
/// The Zammad reads and the one write bulk delete needs, as observed
/// against Zammad 7.3.0 (lab/zammad/README.md):
///  - GET users/me and roles: who the token belongs to;
///  - GET ticket_states: state names, since search results carry IDs;
///  - GET tickets/search?full=true, paged with page and per_page;
///  - DELETE tickets/{id}: immediate and permanent, admin permission only.
///
/// Zammad's search matches loosely, so the result keeps only tickets that
/// exactly match the organization, requester email, domain and sender
/// name asked for. Narrowing can only remove tickets, never add one.
/// </summary>
public sealed class ZammadDeletionProvider
{
    /// <summary>Tickets per search page.</summary>
    public const int PageSize = 100;

    /// <summary>Pages read before a search is declared incomplete.</summary>
    public const int MaximumPages = 50;

    private readonly ZammadApiClient _client;

    public ZammadDeletionProvider(ZammadApiClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public Uri BaseUri => _client.BaseUri;

    /// <summary>
    /// The token's user and role names. Throws ZammadApiException for a
    /// refused token (401) or any other answer.
    /// </summary>
    public async Task<ZammadUser> GetCurrentUserAsync(
        CancellationToken cancellationToken)
    {
        var user =
            await GetJsonAsync<ZammadUserAssetDto>("users/me", cancellationToken)
            ?? throw new InvalidOperationException(
                "Zammad returned no user for the deletion token.");

        if (user.Id is not > 0)
        {
            throw new InvalidOperationException(
                "Zammad returned a user without an ID.");
        }

        string? roles = null;

        try
        {
            var all =
                await GetJsonAsync<List<ZammadNamedDto>>("roles", cancellationToken)
                ?? [];

            var names =
                all.Where(role => role.Id is not null
                                  && user.RoleIds?.Contains(role.Id.Value) == true
                                  && !string.IsNullOrWhiteSpace(role.Name))
                    .Select(role => role.Name!.Trim())
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            roles = names.Length == 0 ? null : string.Join(", ", names);
        }
        catch (Exception exception)
            when (exception is ZammadApiException { StatusCode: not HttpStatusCode.Unauthorized }
                      or JsonException)
        {
            // The role names are for display only.
        }

        var name = string.Join(
            ' ',
            new[] { user.FirstName, user.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part) && part != "-")
                .Select(part => part!.Trim()));

        return new ZammadUser(
            user.Id.Value,
            name.Length == 0 ? null : name,
            string.IsNullOrWhiteSpace(user.Email) ? null : user.Email.Trim(),
            roles);
    }

    /// <summary>
    /// Every ticket matching the criteria, read page by page. Complete only
    /// when the tickets read are exactly as many as Zammad reported.
    /// </summary>
    public async Task<ZammadSearchResult> SearchAsync(
        ZammadSearchCriteria criteria,
        CancellationToken cancellationToken)
    {
        var query = ZammadSearchQuery.Build(criteria);

        var states = await GetStateNamesAsync(cancellationToken);

        var tickets = new Dictionary<long, ZammadTicketAssetDto>();
        var order = new List<long>();
        var users = new Dictionary<long, ZammadUserAssetDto>();
        var organizations = new Dictionary<long, string>();

        int? reported = null;
        var duplicates = 0;
        string? incomplete = null;

        for (var page = 1; ; page++)
        {
            if (page > MaximumPages)
            {
                incomplete =
                    $"More than {MaximumPages * PageSize} tickets match; " +
                    "only the first are read.";
                break;
            }

            var response =
                await GetJsonAsync<ZammadSearchResponseDto>(
                    "tickets/search?query=" + Uri.EscapeDataString(query) +
                    "&full=true&page=" + page.ToString(CultureInfo.InvariantCulture) +
                    "&per_page=" + PageSize.ToString(CultureInfo.InvariantCulture),
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Zammad returned an empty search answer.");

            reported ??= response.TotalCount;

            var ids = response.RecordIds ?? [];

            foreach (var pair in response.Assets?.Users ?? [])
            {
                if (pair.Value.Id is { } id)
                {
                    users[id] = pair.Value;
                }
            }

            foreach (var pair in response.Assets?.Organizations ?? [])
            {
                if (pair.Value.Id is { } id && !string.IsNullOrWhiteSpace(pair.Value.Name))
                {
                    organizations[id] = pair.Value.Name.Trim();
                }
            }

            foreach (var id in ids)
            {
                if (tickets.ContainsKey(id))
                {
                    duplicates++;
                    continue;
                }

                var asset =
                    response.Assets?.Tickets?.GetValueOrDefault(
                        id.ToString(CultureInfo.InvariantCulture));

                if (asset is null)
                {
                    incomplete ??=
                        $"Zammad listed ticket {id} without its details.";
                    continue;
                }

                tickets[id] = asset;
                order.Add(id);
            }

            if (ids.Count < PageSize
                || (reported is { } total && tickets.Count + duplicates >= total))
            {
                break;
            }
        }

        if (reported is null)
        {
            incomplete ??= "Zammad did not report how many tickets match.";
        }
        else if (incomplete is null && tickets.Count != reported.Value)
        {
            incomplete =
                $"Zammad reported {reported.Value} matching tickets but " +
                $"returned {tickets.Count}.";
        }

        var emails =
            users.Values
                .Where(user => user.Id is not null && !string.IsNullOrWhiteSpace(user.Email))
                .ToDictionary(user => user.Id!.Value, user => user.Email!.Trim());

        var exact =
            order
                .Select(id => tickets[id])
                .Where(ticket => MatchesExactly(ticket, criteria, users, organizations))
                .Select(ticket => new ZammadTicket(
                    ticket.Id!.Value,
                    string.IsNullOrWhiteSpace(ticket.Number) ? null : ticket.Number.Trim(),
                    ticket.Title,
                    ticket.StateId is { } stateId && states.TryGetValue(stateId, out var state)
                        ? state
                        : null,
                    ticket.OrganizationId,
                    ticket.CustomerId,
                    ticket.CreatedAt,
                    ticket.UpdatedAt))
                .ToArray();

        return new ZammadSearchResult(
            exact,
            reported,
            duplicates,
            incomplete is null,
            incomplete,
            organizations,
            emails);
    }

    /// <summary>Deletes one ticket and says what Zammad answered.</summary>
    public async Task<ZammadDeleteOutcome> DeleteTicketAsync(
        long ticketId,
        CancellationToken cancellationToken)
    {
        using var response =
            await _client.DeleteTicketAsync(ticketId, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = (int)response.StatusCode;

        var kind =
            response.StatusCode switch
            {
                HttpStatusCode.OK or HttpStatusCode.NoContent => ZammadDeleteStatus.Deleted,
                HttpStatusCode.NotFound => ZammadDeleteStatus.NotFound,
                HttpStatusCode.Unauthorized => ZammadDeleteStatus.TokenRefused,
                HttpStatusCode.RequestTimeout => ZammadDeleteStatus.Unclear,
                _ when status is >= 400 and < 500 => ZammadDeleteStatus.Refused,
                _ => ZammadDeleteStatus.Unclear
            };

        return new ZammadDeleteOutcome(kind, status, ZammadErrorText.From(body));
    }

    private async Task<IReadOnlyDictionary<long, string>> GetStateNamesAsync(
        CancellationToken cancellationToken)
    {
        var states =
            await GetJsonAsync<List<ZammadNamedDto>>("ticket_states", cancellationToken)
            ?? [];

        return states
            .Where(state => state.Id is not null && !string.IsNullOrWhiteSpace(state.Name))
            .ToDictionary(state => state.Id!.Value, state => state.Name!.Trim());
    }

    private static bool MatchesExactly(
        ZammadTicketAssetDto ticket,
        ZammadSearchCriteria criteria,
        IReadOnlyDictionary<long, ZammadUserAssetDto> users,
        IReadOnlyDictionary<long, string> organizations)
    {
        if (ticket.Id is null)
        {
            return false;
        }

        var customer =
            ticket.CustomerId is { } customerId
                ? users.GetValueOrDefault(customerId)
                : null;

        var email = customer?.Email?.Trim();

        if (!string.IsNullOrWhiteSpace(criteria.Organization)
            && !(ticket.OrganizationId is { } organizationId
                 && organizations.TryGetValue(organizationId, out var organization)
                 && Same(organization, criteria.Organization)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(criteria.RequesterEmail)
            && !Same(email, criteria.RequesterEmail))
        {
            return false;
        }

        if (ZammadSearchQuery.Domain(criteria.EmailDomain) is { } domain
            && !(email is not null
                 && email.EndsWith("@" + domain, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(criteria.SenderName))
        {
            var fullName =
                string.Join(
                    ' ',
                    new[] { customer?.FirstName, customer?.LastName }
                        .Where(part => !string.IsNullOrWhiteSpace(part))
                        .Select(part => part!.Trim()));

            if (!Same(fullName, criteria.SenderName))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Same(string? left, string? right) =>
        left is not null
        && right is not null
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private async Task<T?> GetJsonAsync<T>(
        string pathAndQuery,
        CancellationToken cancellationToken)
        where T : class
    {
        using var response = await _client.GetAsync(pathAndQuery, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new ZammadApiException(
                response.StatusCode,
                $"Zammad answered HTTP {(int)response.StatusCode}: " +
                ZammadErrorText.From(body));
        }

        return JsonSerializer.Deserialize<T>(body);
    }
}
