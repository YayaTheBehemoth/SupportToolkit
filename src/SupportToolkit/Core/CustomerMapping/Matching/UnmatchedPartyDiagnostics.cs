namespace SupportToolkit.Core.CustomerMapping.Matching;

/// <summary>
/// Why a party ended up without an accepted link.
/// </summary>
public enum UnmatchedReason
{
    /// <summary>
    /// The resolver saw several equally good candidates, or a
    /// conflict between keys, and refused to guess.
    /// </summary>
    AmbiguousOrConflicting,

    /// <summary>
    /// The best candidate shares only some words, which is too weak
    /// to accept automatically.
    /// </summary>
    OnlyWeakNameOverlap,

    /// <summary>
    /// A fitting candidate exists but is already linked to another
    /// party.
    /// </summary>
    CandidateTakenByAnotherParty,

    /// <summary>
    /// A similarly named customer exists on the other side but is not
    /// among the customers that were matched (it holds none of the
    /// service items). The service is probably not billed for it, or
    /// it is billed under another item.
    /// </summary>
    SimilarCustomerNotInScope,

    /// <summary>
    /// Nothing on the other side looks like this party.
    /// </summary>
    NoSimilarCustomer
}

public sealed record UnmatchedCandidate(
    string CustomerId,
    string? CustomerName,
    LooseNameTier Tier,
    bool InMatchedSet,
    IReadOnlyList<string> Items);

public sealed record UnmatchedPartyDiagnosis(
    string PartyId,
    string? PartyName,
    UnmatchedReason Reason,
    IReadOnlyList<UnmatchedCandidate> Candidates);

/// <summary>
/// Explains, per unlinked party, the most likely reason. Read-only
/// analysis for developers: it decides nothing and links nothing.
/// </summary>
public static class UnmatchedPartyDiagnostics
{
    private const int MaxCandidates = 3;

    /// <param name="unlinked">Parties without an accepted link.</param>
    /// <param name="candidatePool">
    /// Every customer on the other side, matched or not, so that a
    /// similar customer outside the matched set can be found.
    /// </param>
    /// <param name="matchedSet">Ids that took part in matching.</param>
    /// <param name="linkedCustomerIds">
    /// Customers already linked to some party.
    /// </param>
    /// <param name="flaggedPartyIds">
    /// Parties the resolver reported as ambiguous or conflicting.
    /// </param>
    /// <param name="itemsByCustomer">
    /// Item numbers each customer holds, for context.
    /// </param>
    public static IReadOnlyList<UnmatchedPartyDiagnosis> Diagnose(
        IReadOnlyList<CustomerParty> unlinked,
        IReadOnlyList<CustomerParty> candidatePool,
        IReadOnlySet<string> matchedSet,
        IReadOnlySet<string> linkedCustomerIds,
        IReadOnlySet<string> flaggedPartyIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> itemsByCustomer)
    {
        ArgumentNullException.ThrowIfNull(unlinked);
        ArgumentNullException.ThrowIfNull(candidatePool);
        ArgumentNullException.ThrowIfNull(matchedSet);
        ArgumentNullException.ThrowIfNull(linkedCustomerIds);
        ArgumentNullException.ThrowIfNull(flaggedPartyIds);
        ArgumentNullException.ThrowIfNull(itemsByCustomer);

        return unlinked
            .OrderBy(party => party.Name, StringComparer.OrdinalIgnoreCase)
            .Select(
                party => Diagnose(
                    party,
                    candidatePool,
                    matchedSet,
                    linkedCustomerIds,
                    flaggedPartyIds,
                    itemsByCustomer))
            .ToArray();
    }

    private static UnmatchedPartyDiagnosis Diagnose(
        CustomerParty party,
        IReadOnlyList<CustomerParty> pool,
        IReadOnlySet<string> matchedSet,
        IReadOnlySet<string> linkedCustomerIds,
        IReadOnlySet<string> flaggedPartyIds,
        IReadOnlyDictionary<string, IReadOnlyList<string>> itemsByCustomer)
    {
        var candidates =
            pool
                .Select(
                    customer => (
                        Customer: customer,
                        Tier: LooseNameMatcher.Compare(
                            party.Name,
                            customer.Name)))
                .Where(item => item.Tier != LooseNameTier.None)
                .OrderByDescending(item => item.Tier)
                .ThenBy(
                    item => item.Customer.Name,
                    StringComparer.OrdinalIgnoreCase)
                .Take(MaxCandidates)
                .Select(
                    item => new UnmatchedCandidate(
                        item.Customer.Id.Trim(),
                        item.Customer.Name,
                        item.Tier,
                        matchedSet.Contains(item.Customer.Id.Trim()),
                        itemsByCustomer.GetValueOrDefault(
                            item.Customer.Id.Trim())
                            ?? []))
                .ToArray();

        return new UnmatchedPartyDiagnosis(
            party.Id.Trim(),
            party.Name,
            Classify(
                party,
                candidates,
                linkedCustomerIds,
                flaggedPartyIds),
            candidates);
    }

    private static UnmatchedReason Classify(
        CustomerParty party,
        IReadOnlyList<UnmatchedCandidate> candidates,
        IReadOnlySet<string> linkedCustomerIds,
        IReadOnlySet<string> flaggedPartyIds)
    {
        if (flaggedPartyIds.Contains(party.Id.Trim()))
        {
            return UnmatchedReason.AmbiguousOrConflicting;
        }

        if (candidates.Count == 0)
        {
            return UnmatchedReason.NoSimilarCustomer;
        }

        var best = candidates[0];

        if (!best.InMatchedSet)
        {
            return UnmatchedReason.SimilarCustomerNotInScope;
        }

        if (linkedCustomerIds.Contains(best.CustomerId))
        {
            return UnmatchedReason.CandidateTakenByAnotherParty;
        }

        return best.Tier == LooseNameTier.Overlap
            ? UnmatchedReason.OnlyWeakNameOverlap
            : UnmatchedReason.AmbiguousOrConflicting;
    }
}
