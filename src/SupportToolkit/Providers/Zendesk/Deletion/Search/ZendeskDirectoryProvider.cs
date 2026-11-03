using SupportToolkit.Providers.Zendesk.Deletion.Search.Dtos;
using SupportToolkit.Providers.Zendesk.Deletion.Transport;

namespace SupportToolkit.Providers.Zendesk.Deletion.Search;

/// <summary>
/// The signed-in Zendesk user. Shown to the operator and recorded in the
/// later receipt; never logged.
/// </summary>
public sealed record ZendeskOperator(
    long Id,
    string? Name,
    string? Email,
    string? Role);

/// <summary>
/// Read-only lookups for the signed-in user and for the names behind the
/// ticket search results. Zendesk search results carry only organization
/// and requester IDs, so the preview needs these to group by organization
/// and requester domain.
///
/// An ID Zendesk does not return is absent from the result: unknown.
/// </summary>
public sealed class ZendeskDirectoryProvider
{
    public const int LookupBatchSize = 100;

    private readonly ZendeskDeletionApiClient _client;

    public ZendeskDirectoryProvider(
        ZendeskDeletionApiClient client)
    {
        _client =
            client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<ZendeskOperator> GetCurrentUserAsync(
        CancellationToken cancellationToken = default)
    {
        var dto =
            await _client.GetJsonAsync<ZendeskCurrentUserDto>(
                "/api/v2/users/me.json",
                cancellationToken);

        if (dto.User?.Id is not > 0)
        {
            throw new InvalidOperationException(
                "Zendesk did not identify the signed-in user.");
        }

        return new ZendeskOperator(
            dto.User.Id.Value,
            dto.User.Name,
            dto.User.Email,
            dto.User.Role);
    }

    /// <summary>User ID to email address.</summary>
    public async Task<IReadOnlyDictionary<long, string>> GetUserEmailsAsync(
        IEnumerable<long> userIds,
        CancellationToken cancellationToken = default)
    {
        var emails = new Dictionary<long, string>();

        foreach (var batch in Batches(userIds))
        {
            var dto =
                await _client.GetJsonAsync<ZendeskUsersDto>(
                    "/api/v2/users/show_many.json?ids=" +
                    string.Join(",", batch),
                    cancellationToken);

            foreach (var user in dto.Users)
            {
                if (user.Id is > 0
                    && !string.IsNullOrWhiteSpace(user.Email))
                {
                    emails[user.Id.Value] = user.Email.Trim();
                }
            }
        }

        return emails;
    }

    /// <summary>Organization ID to name.</summary>
    public async Task<IReadOnlyDictionary<long, string>>
        GetOrganizationNamesAsync(
            IEnumerable<long> organizationIds,
            CancellationToken cancellationToken = default)
    {
        var names = new Dictionary<long, string>();

        foreach (var batch in Batches(organizationIds))
        {
            var dto =
                await _client.GetJsonAsync<ZendeskOrganizationsDto>(
                    "/api/v2/organizations/show_many.json?ids=" +
                    string.Join(",", batch),
                    cancellationToken);

            foreach (var organization in dto.Organizations)
            {
                if (organization.Id is > 0
                    && !string.IsNullOrWhiteSpace(organization.Name))
                {
                    names[organization.Id.Value] = organization.Name.Trim();
                }
            }
        }

        return names;
    }

    private static IEnumerable<long[]> Batches(
        IEnumerable<long> ids) =>
        ids.Where(id => id > 0)
            .Distinct()
            .Order()
            .Chunk(LookupBatchSize);
}
