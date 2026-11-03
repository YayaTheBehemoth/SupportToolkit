namespace SupportToolkit.Core.CustomerMapping.Matching;

/// <summary>
/// A tenant and a customer whose names are alike without being equal.
/// </summary>
public sealed record LooseNameCandidate(
    string AcronisTenantId,
    string BcCustomerNo,
    LooseNameTier Tier
);

public sealed record LooseNameRankingResult(
    IReadOnlyList<CustomerMappingRecord> Added,
    IReadOnlyList<CustomerMatchException> Exceptions,
    IReadOnlyList<LooseNameCandidate> Candidates
);

/// <summary>
/// Fills the gaps the deterministic keys leave, using loose name
/// similarity.
///
/// Run after CustomerMappingSuggester: parties already in a Suggested
/// or Confirmed mapping, and Rejected pairs, are skipped, so a
/// stronger key is never overridden. A Suggested record is written
/// only when a pair is the unique best candidate for both sides at an
/// Exact, NearExact or Containment tier. Every other candidate, including every
/// Overlap, is reported as an exception for a person to resolve.
///
/// Nothing is confirmed here.
/// </summary>
public sealed class CustomerMappingCandidateRanker
{
    private readonly ICustomerMappingRepository _repository;

    private readonly TimeProvider _timeProvider;

    public CustomerMappingCandidateRanker(
        ICustomerMappingRepository repository,
        TimeProvider? timeProvider = null)
    {
        _repository =
            repository
            ?? throw new ArgumentNullException(nameof(repository));

        _timeProvider =
            timeProvider
            ?? TimeProvider.System;
    }

    public static double ScoreFor(
        LooseNameTier tier)
    {
        return tier switch
        {
            LooseNameTier.Exact => 0.45,
            LooseNameTier.NearExact => 0.4,
            LooseNameTier.Containment => 0.35,
            LooseNameTier.Overlap => 0.2,
            _ => throw new ArgumentOutOfRangeException(nameof(tier))
        };
    }

    public async Task<LooseNameRankingResult> RankAsync(
        IReadOnlyList<CustomerParty> acronis,
        IReadOnlyList<CustomerParty> businessCentral,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acronis);
        ArgumentNullException.ThrowIfNull(businessCentral);

        var existing =
            await _repository.GetAllAsync(cancellationToken);

        var occupied =
            existing
                .Where(
                    mapping =>
                        mapping.State
                            is CustomerMappingState.Suggested
                            or CustomerMappingState.Confirmed)
                .ToArray();

        var occupiedTenants =
            occupied
                .Select(mapping => mapping.AcronisTenantId.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var occupiedCustomers =
            occupied
                .Select(mapping => mapping.BcCustomerNo.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rejected =
            existing
                .Where(
                    mapping =>
                        mapping.State == CustomerMappingState.Rejected)
                .Select(
                    mapping => Pair(
                        mapping.AcronisTenantId,
                        mapping.BcCustomerNo))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates =
            new List<LooseNameCandidate>();

        foreach (var tenant in acronis
            .Where(
                party => !occupiedTenants.Contains(party.Id.Trim())))
        {
            foreach (var customer in businessCentral
                .Where(
                    party => !occupiedCustomers.Contains(party.Id.Trim())))
            {
                if (rejected.Contains(Pair(tenant.Id, customer.Id)))
                {
                    continue;
                }

                var tier =
                    LooseNameMatcher.Compare(
                        tenant.Name,
                        customer.Name);

                if (tier != LooseNameTier.None)
                {
                    candidates.Add(
                        new LooseNameCandidate(
                            tenant.Id.Trim(),
                            customer.Id.Trim(),
                            tier));
                }
            }
        }

        var bestPerTenant =
            BestTier(candidates, candidate => candidate.AcronisTenantId);

        var bestPerCustomer =
            BestTier(candidates, candidate => candidate.BcCustomerNo);

        var added =
            new List<CustomerMappingRecord>();

        var exceptions =
            new List<CustomerMatchException>();

        var now =
            _timeProvider.GetUtcNow();

        foreach (var candidate in candidates
            .OrderBy(
                candidate => candidate.AcronisTenantId,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                candidate => candidate.BcCustomerNo,
                StringComparer.OrdinalIgnoreCase))
        {
            var tenantTops =
                Tops(candidates, bestPerTenant, candidate.AcronisTenantId,
                    byTenant: true);

            var customerTops =
                Tops(candidates, bestPerCustomer, candidate.BcCustomerNo,
                    byTenant: false);

            var isBestForBoth =
                candidate.Tier == bestPerTenant[candidate.AcronisTenantId]
                && candidate.Tier == bestPerCustomer[candidate.BcCustomerNo];

            var unique =
                tenantTops.Count == 1
                && customerTops.Count == 1
                && isBestForBoth;

            if (unique
                && candidate.Tier
                    is LooseNameTier.Exact
                        or LooseNameTier.NearExact
                        or LooseNameTier.Containment)
            {
                var suggestion =
                    new CustomerMappingRecord(
                        Guid.NewGuid(),
                        candidate.AcronisTenantId,
                        candidate.BcCustomerNo,
                        CustomerMappingState.Suggested,
                        CustomerMatchMethod.LooseName,
                        ScoreFor(candidate.Tier),
                        SuggestedAt: now,
                        Notes: $"Loose name match: {candidate.Tier}");

                await _repository.AddAsync(
                    suggestion,
                    cancellationToken);

                added.Add(suggestion);
                continue;
            }

            // Only report a candidate that is a top choice for at least
            // one side; lesser alternatives would only add noise.
            if (candidate.Tier != bestPerTenant[candidate.AcronisTenantId]
                && candidate.Tier != bestPerCustomer[candidate.BcCustomerNo])
            {
                continue;
            }

            exceptions.Add(
                new CustomerMatchException(
                    unique
                        ? CustomerMatchExceptionKind.WeakNameCandidate
                        : (customerTops.Count, tenantTops.Count) switch
                        {
                            (1, _) => CustomerMatchExceptionKind.OneToMany,
                            (_, 1) => CustomerMatchExceptionKind.ManyToOne,
                            _ => CustomerMatchExceptionKind.ManyToMany
                        },
                    CustomerMatchKey.Name,
                    [candidate.AcronisTenantId],
                    [candidate.BcCustomerNo]));
        }

        return new LooseNameRankingResult(
            added,
            exceptions,
            candidates);
    }

    private static Dictionary<string, LooseNameTier> BestTier(
        IReadOnlyList<LooseNameCandidate> candidates,
        Func<LooseNameCandidate, string> key)
    {
        return candidates
            .GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Max(candidate => candidate.Tier),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<LooseNameCandidate> Tops(
        IReadOnlyList<LooseNameCandidate> candidates,
        Dictionary<string, LooseNameTier> best,
        string id,
        bool byTenant)
    {
        return candidates
            .Where(
                candidate =>
                    string.Equals(
                        byTenant
                            ? candidate.AcronisTenantId
                            : candidate.BcCustomerNo,
                        id,
                        StringComparison.OrdinalIgnoreCase)
                    && candidate.Tier == best[id])
            .ToArray();
    }

    private static string Pair(
        string tenant,
        string customer)
    {
        return tenant.Trim() + "\u001F" + customer.Trim();
    }
}
