using SupportToolkit.Core.CustomerMapping.Matching;

namespace SupportToolkit.Core.CustomerMapping;

/// <summary>
/// When a suggestion is accepted without a person.
///
/// A suggestion is auto-accepted only when it is unique and mutual
/// (that is already guaranteed by the suggester and ranker) and its
/// score reaches MinimumScore. With the default, account number,
/// CVR, e-mail domain, exact name, an exact loose name and a
/// containment name (one name inside the other) are accepted; only a
/// weak overlap candidate is left for a person. Ambiguous and
/// conflicting cases are never accepted at any score.
/// </summary>
public sealed record AutoMappingPolicy(
    double MinimumScore = 0.35)
{
    public static AutoMappingPolicy Default { get; } = new();

    /// <summary>Accepts nothing; every suggestion is left to a person.</summary>
    public static AutoMappingPolicy None { get; } =
        new(double.PositiveInfinity);
}

/// <summary>
/// The outcome of one resolution run.
///
/// Accepted links are Confirmed mappings recorded as made by the
/// automatic actor. PendingReview are suggestions below the policy
/// threshold. The two Unmatched lists hold every party with neither
/// an accepted link nor a pending suggestion.
/// </summary>
public sealed record AutoMappingResult(
    IReadOnlyList<CustomerMappingRecord> Accepted,
    IReadOnlyList<CustomerMappingRecord> PendingReview,
    IReadOnlyList<CustomerMatchException> Exceptions,
    IReadOnlyList<string> UnmatchedAcronisIds,
    IReadOnlyList<string> UnmatchedBcCustomerNumbers)
{
    public IReadOnlyList<AutoMappingPair> Pairs =>
        Accepted
            .Select(
                mapping => new AutoMappingPair(
                    mapping.BcCustomerNo.Trim(),
                    mapping.AcronisTenantId.Trim()))
            .ToArray();
}

public sealed record AutoMappingPair(
    string BcCustomerNumber,
    string AcronisTenantId);

/// <summary>
/// Resolves Acronis tenants to Business Central customers on every
/// run, deterministically and without stored state.
///
/// It reuses the suggester (strong keys, exact name) and the loose
/// name ranker, then confirms suggestions that meet the policy. A
/// stored mapping, once a backend exists, can be passed in as the
/// repository; stored human decisions then take precedence because
/// suggested and confirmed parties are skipped.
/// </summary>
public sealed class AutoCustomerMappingResolver
{
    public const string AutomaticActor = "auto-mapping";

    private readonly AutoMappingPolicy _policy;

    private readonly TimeProvider? _timeProvider;

    public AutoCustomerMappingResolver(
        AutoMappingPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        _policy =
            policy
            ?? AutoMappingPolicy.Default;

        _timeProvider =
            timeProvider;
    }

    public async Task<AutoMappingResult> ResolveAsync(
        IReadOnlyList<CustomerParty> acronis,
        IReadOnlyList<CustomerParty> businessCentral,
        ICustomerMappingRepository? repository = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acronis);
        ArgumentNullException.ThrowIfNull(businessCentral);

        repository ??=
            new InMemoryCustomerMappingRepository();

        var suggestions =
            await new CustomerMappingSuggester(repository, _timeProvider)
                .SuggestAsync(
                    acronis,
                    businessCentral,
                    cancellationToken);

        // A party the stronger keys flagged as conflicting or
        // ambiguous stays with a person; a weaker key must not
        // resolve what a stronger one could not.
        var flaggedTenants =
            suggestions.Exceptions
                .SelectMany(exception => exception.AcronisTenantIds)
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var flaggedCustomers =
            suggestions.Exceptions
                .SelectMany(exception => exception.BcCustomerNumbers)
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var loose =
            await new CustomerMappingCandidateRanker(
                repository,
                _timeProvider)
                .RankAsync(
                    acronis
                        .Where(
                            party => !flaggedTenants.Contains(
                                party.Id.Trim()))
                        .ToArray(),
                    businessCentral
                        .Where(
                            party => !flaggedCustomers.Contains(
                                party.Id.Trim()))
                        .ToArray(),
                    cancellationToken);

        var confirmation =
            new CustomerMappingConfirmationService(
                repository,
                _timeProvider);

        var pending =
            new List<CustomerMappingRecord>();

        foreach (var suggestion in suggestions.Added
            .Concat(loose.Added)
            .OrderBy(
                mapping => mapping.AcronisTenantId,
                StringComparer.OrdinalIgnoreCase))
        {
            if ((suggestion.MatchScore ?? 0) < _policy.MinimumScore)
            {
                pending.Add(suggestion);
                continue;
            }

            try
            {
                await confirmation.ConfirmAsync(
                    suggestion.Id,
                    AutomaticActor,
                    "Accepted automatically by policy.",
                    cancellationToken);
            }
            catch (InvalidOperationException)
            {
                // A conflicting confirmed link exists; leave it to review.
                pending.Add(suggestion);
            }
        }

        var all =
            await repository.GetAllAsync(cancellationToken);

        var accepted =
            all
                .Where(
                    mapping =>
                        mapping.State == CustomerMappingState.Confirmed
                        && mapping.ConfirmedBy == AutomaticActor)
                .OrderBy(
                    mapping => mapping.AcronisTenantId,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        // A party with a confirmed link or a pending suggestion has a
        // candidate; only parties with neither count as unmatched.
        var linkedTenants =
            all
                .Where(
                    mapping => mapping.State
                        is CustomerMappingState.Confirmed
                        or CustomerMappingState.Suggested)
                .Select(mapping => mapping.AcronisTenantId.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var linkedCustomers =
            all
                .Where(
                    mapping => mapping.State
                        is CustomerMappingState.Confirmed
                        or CustomerMappingState.Suggested)
                .Select(mapping => mapping.BcCustomerNo.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new AutoMappingResult(
            accepted,
            pending,
            [.. suggestions.Exceptions, .. loose.Exceptions],
            acronis
                .Select(party => party.Id.Trim())
                .Where(id => !linkedTenants.Contains(id))
                .ToArray(),
            businessCentral
                .Select(party => party.Id.Trim())
                .Where(id => !linkedCustomers.Contains(id))
                .ToArray());
    }
}
