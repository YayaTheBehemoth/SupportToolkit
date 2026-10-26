namespace SupportToolkit.Core.CustomerMapping.Matching;

/// <summary>
/// Counts for one key on one side. Contains no source values.
/// </summary>
public sealed record KeySideProfile(
    int Records,
    int Ok,
    int Empty,
    int Malformed,
    int Excluded
);

/// <summary>
/// Count-only profile of one candidate key.
///
/// The *Values counts are distinct normalized key values grouped by
/// cardinality. The *RecordsWithoutMatch counts are records whose
/// usable key occurs only on their own side.
///
/// This type intentionally has no member that can carry a source
/// value, so its output is safe to report.
/// </summary>
public sealed record CandidateKeyProfile(
    CustomerMatchKey Key,
    KeySideProfile Acronis,
    KeySideProfile BusinessCentral,
    int OneToOneValues,
    int OneToManyValues,
    int ManyToOneValues,
    int ManyToManyValues,
    int AcronisRecordsWithoutMatch,
    int BusinessCentralRecordsWithoutMatch
);

/// <summary>
/// Read-only evaluation of which candidate key yields the most clean
/// one-to-one matches.
/// </summary>
public static class CandidateKeyProfiler
{
    public static IReadOnlyList<CandidateKeyProfile> Profile(
        IReadOnlyList<CustomerParty> acronis,
        IReadOnlyList<CustomerParty> businessCentral)
    {
        ArgumentNullException.ThrowIfNull(acronis);
        ArgumentNullException.ThrowIfNull(businessCentral);

        return Enum.GetValues<CustomerMatchKey>()
            .Select(
                key => Profile(
                    key,
                    acronis,
                    businessCentral
                )
            )
            .ToArray();
    }

    public static CandidateKeyProfile Profile(
        CustomerMatchKey key,
        IReadOnlyList<CustomerParty> acronis,
        IReadOnlyList<CustomerParty> businessCentral)
    {
        var acronisKeys =
            Normalize(key, acronis);

        var businessCentralKeys =
            Normalize(key, businessCentral);

        var acronisCounts =
            CountByValue(acronisKeys);

        var businessCentralCounts =
            CountByValue(businessCentralKeys);

        int oneToOne = 0;
        int oneToMany = 0;
        int manyToOne = 0;
        int manyToMany = 0;
        int acronisWithoutMatch = 0;
        int businessCentralWithoutMatch = 0;

        foreach (var value in acronisCounts.Keys
            .Union(businessCentralCounts.Keys))
        {
            var a =
                acronisCounts.GetValueOrDefault(value);

            var b =
                businessCentralCounts.GetValueOrDefault(value);

            switch (a, b)
            {
                case (_, 0):
                    acronisWithoutMatch += a;
                    break;
                case (0, _):
                    businessCentralWithoutMatch += b;
                    break;
                case (1, 1):
                    oneToOne++;
                    break;
                case (1, _):
                    oneToMany++;
                    break;
                case (_, 1):
                    manyToOne++;
                    break;
                default:
                    manyToMany++;
                    break;
            }
        }

        return new CandidateKeyProfile(
            key,
            Side(acronisKeys),
            Side(businessCentralKeys),
            oneToOne,
            oneToMany,
            manyToOne,
            manyToMany,
            acronisWithoutMatch,
            businessCentralWithoutMatch
        );
    }

    internal static IReadOnlyList<NormalizedKey> Normalize(
        CustomerMatchKey key,
        IReadOnlyList<CustomerParty> parties)
    {
        return parties
            .Select(
                party => PartyKeyNormalizer.Normalize(
                    key,
                    party
                )
            )
            .ToArray();
    }

    private static Dictionary<string, int> CountByValue(
        IReadOnlyList<NormalizedKey> keys)
    {
        return keys
            .Where(
                normalized =>
                    normalized.Status == NormalizedKeyStatus.Ok
            )
            .GroupBy(
                normalized => normalized.Value!,
                StringComparer.Ordinal
            )
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal
            );
    }

    private static KeySideProfile Side(
        IReadOnlyList<NormalizedKey> keys)
    {
        int Count(NormalizedKeyStatus status) =>
            keys.Count(
                normalized => normalized.Status == status
            );

        return new KeySideProfile(
            keys.Count,
            Count(NormalizedKeyStatus.Ok),
            Count(NormalizedKeyStatus.Empty),
            Count(NormalizedKeyStatus.Malformed),
            Count(NormalizedKeyStatus.Excluded)
        );
    }
}
