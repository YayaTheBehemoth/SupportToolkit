namespace SupportToolkit.Core.CustomerMapping.Matching;

public enum CustomerMatchExceptionKind
{
    OneToMany,
    ManyToOne,
    ManyToMany,

    /// <summary>
    /// Different keys link a party to different partners.
    /// </summary>
    KeyConflict,

    /// <summary>
    /// A unique but weak loose-name candidate that is not suggested
    /// automatically.
    /// </summary>
    WeakNameCandidate
}

/// <summary>
/// A candidate that cannot be suggested automatically and needs a
/// manual mapping decision.
///
/// Party ids are carried so a reviewer can act; at runtime they are
/// real identifiers and must stay in the runtime environment.
/// </summary>
public sealed record CustomerMatchException(
    CustomerMatchExceptionKind Kind,
    CustomerMatchKey? Key,
    IReadOnlyList<string> AcronisTenantIds,
    IReadOnlyList<string> BcCustomerNumbers
);

public sealed record CustomerMappingSuggestionResult(
    IReadOnlyList<CustomerMappingRecord> Added,
    IReadOnlyList<CustomerMatchException> Exceptions
);

/// <summary>
/// Generates Suggested mappings by deterministic entity resolution.
///
/// Keys are evaluated from strongest to weakest: CVR, e-mail domain,
/// name. A pair is suggested only when it is one-to-one on a key, no
/// stronger key places either party in an ambiguous group, no other
/// key links either party to a different partner, neither party is
/// already in a Suggested or Confirmed mapping, and the pair was not
/// previously Rejected.
///
/// Nothing is confirmed here; every suggestion needs a person.
/// </summary>
public sealed class CustomerMappingSuggester
{
    private readonly ICustomerMappingRepository _repository;

    private readonly TimeProvider _timeProvider;

    public CustomerMappingSuggester(
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
        CustomerMatchKey key)
    {
        return key switch
        {
            CustomerMatchKey.Cvr => 1.0,
            CustomerMatchKey.EmailDomain => 0.8,
            CustomerMatchKey.Name => 0.5,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
    }

    public async Task<CustomerMappingSuggestionResult> SuggestAsync(
        IReadOnlyList<CustomerParty> acronis,
        IReadOnlyList<CustomerParty> businessCentral,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acronis);
        ArgumentNullException.ThrowIfNull(businessCentral);

        RequireUniqueIds(acronis, "Acronis");
        RequireUniqueIds(businessCentral, "Business Central");

        var existing =
            await _repository.GetAllAsync(cancellationToken);

        var occupied =
            existing
                .Where(
                    mapping =>
                        mapping.State
                            is CustomerMappingState.Suggested
                            or CustomerMappingState.Confirmed
                )
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
                        mapping.State == CustomerMappingState.Rejected
                )
                .Select(
                    mapping => PairKey(
                        mapping.AcronisTenantId,
                        mapping.BcCustomerNo
                    )
                )
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var exceptions =
            new List<CustomerMatchException>();

        var pairsByKey =
            new List<(CustomerMatchKey Key, string Tenant, string Customer)>();

        // Parties in an ambiguous group, per key.
        var ambiguousTenants =
            new Dictionary<CustomerMatchKey, HashSet<string>>();

        var ambiguousCustomers =
            new Dictionary<CustomerMatchKey, HashSet<string>>();

        foreach (var key in Enum.GetValues<CustomerMatchKey>())
        {
            ambiguousTenants[key] =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ambiguousCustomers[key] =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var acronisGroups =
                GroupByKey(key, acronis);

            var businessCentralGroups =
                GroupByKey(key, businessCentral);

            foreach (var (value, tenants) in acronisGroups
                .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                if (!businessCentralGroups.TryGetValue(
                    value,
                    out var customers))
                {
                    continue;
                }

                if (tenants.Count == 1 && customers.Count == 1)
                {
                    pairsByKey.Add((key, tenants[0], customers[0]));
                    continue;
                }

                ambiguousTenants[key].UnionWith(tenants);
                ambiguousCustomers[key].UnionWith(customers);

                exceptions.Add(
                    new CustomerMatchException(
                        (tenants.Count, customers.Count) switch
                        {
                            (1, _) => CustomerMatchExceptionKind.OneToMany,
                            (_, 1) => CustomerMatchExceptionKind.ManyToOne,
                            _ => CustomerMatchExceptionKind.ManyToMany
                        },
                        key,
                        tenants,
                        customers
                    )
                );
            }
        }

        var conflicted =
            FindKeyConflicts(
                pairsByKey,
                exceptions
            );

        var added =
            new List<CustomerMappingRecord>();

        var now =
            _timeProvider.GetUtcNow();

        foreach (var (key, tenant, customer) in pairsByKey)
        {
            if (conflicted.Contains(tenant)
                || conflicted.Contains(customer)
                || occupiedTenants.Contains(tenant)
                || occupiedCustomers.Contains(customer)
                || rejected.Contains(PairKey(tenant, customer))
                || IsAmbiguousOnStrongerKey(
                    key,
                    tenant,
                    customer,
                    ambiguousTenants,
                    ambiguousCustomers))
            {
                continue;
            }

            var suggestion =
                new CustomerMappingRecord(
                    Guid.NewGuid(),
                    tenant,
                    customer,
                    CustomerMappingState.Suggested,
                    ToMethod(key),
                    ScoreFor(key),
                    SuggestedAt: now
                );

            await _repository.AddAsync(
                suggestion,
                cancellationToken
            );

            added.Add(suggestion);

            // Weaker keys agreeing on the same pair add nothing.
            occupiedTenants.Add(tenant);
            occupiedCustomers.Add(customer);
        }

        return new CustomerMappingSuggestionResult(
            added,
            exceptions
        );
    }

    /// <summary>
    /// Returns every party id linked by one-to-one pairs to more than
    /// one partner across keys, and records an exception for each.
    /// Ids from both sides share one set; they never collide in
    /// practice and a collision would only make selection stricter.
    /// </summary>
    private static HashSet<string> FindKeyConflicts(
        IReadOnlyList<(CustomerMatchKey Key, string Tenant, string Customer)> pairs,
        List<CustomerMatchException> exceptions)
    {
        var conflicted =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in pairs
            .GroupBy(
                pair => pair.Tenant,
                StringComparer.OrdinalIgnoreCase))
        {
            var partners =
                group
                    .Select(pair => pair.Customer)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            if (partners.Length > 1)
            {
                conflicted.Add(group.Key);
                conflicted.UnionWith(partners);

                exceptions.Add(
                    new CustomerMatchException(
                        CustomerMatchExceptionKind.KeyConflict,
                        null,
                        [group.Key],
                        partners
                    )
                );
            }
        }

        foreach (var group in pairs
            .GroupBy(
                pair => pair.Customer,
                StringComparer.OrdinalIgnoreCase))
        {
            var partners =
                group
                    .Select(pair => pair.Tenant)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            if (partners.Length > 1)
            {
                conflicted.Add(group.Key);
                conflicted.UnionWith(partners);

                exceptions.Add(
                    new CustomerMatchException(
                        CustomerMatchExceptionKind.KeyConflict,
                        null,
                        partners,
                        [group.Key]
                    )
                );
            }
        }

        return conflicted;
    }

    private static bool IsAmbiguousOnStrongerKey(
        CustomerMatchKey key,
        string tenant,
        string customer,
        Dictionary<CustomerMatchKey, HashSet<string>> ambiguousTenants,
        Dictionary<CustomerMatchKey, HashSet<string>> ambiguousCustomers)
    {
        return Enum.GetValues<CustomerMatchKey>()
            .Where(stronger => stronger < key)
            .Any(
                stronger =>
                    ambiguousTenants[stronger].Contains(tenant)
                    || ambiguousCustomers[stronger].Contains(customer)
            );
    }

    private static Dictionary<string, IReadOnlyList<string>> GroupByKey(
        CustomerMatchKey key,
        IReadOnlyList<CustomerParty> parties)
    {
        return parties
            .Select(
                party => (
                    Id: party.Id.Trim(),
                    Normalized: PartyKeyNormalizer.Normalize(key, party)
                )
            )
            .Where(
                item => item.Normalized.Status == NormalizedKeyStatus.Ok
            )
            .GroupBy(
                item => item.Normalized.Value!,
                StringComparer.Ordinal
            )
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(item => item.Id)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.Ordinal
            );
    }

    private static void RequireUniqueIds(
        IReadOnlyList<CustomerParty> parties,
        string side)
    {
        if (parties.Any(party => string.IsNullOrWhiteSpace(party?.Id)))
        {
            throw new ArgumentException(
                $"Every {side} party must have an id."
            );
        }

        if (parties
            .GroupBy(
                party => party.Id.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                $"{side} party ids must be unique."
            );
        }
    }

    private static string PairKey(
        string tenant,
        string customer)
    {
        return tenant.Trim() + "\u001F" + customer.Trim();
    }

    private static CustomerMatchMethod ToMethod(
        CustomerMatchKey key)
    {
        return key switch
        {
            CustomerMatchKey.Cvr => CustomerMatchMethod.Cvr,
            CustomerMatchKey.EmailDomain => CustomerMatchMethod.EmailDomain,
            CustomerMatchKey.Name => CustomerMatchMethod.Name,
            _ => throw new ArgumentOutOfRangeException(nameof(key))
        };
    }
}
