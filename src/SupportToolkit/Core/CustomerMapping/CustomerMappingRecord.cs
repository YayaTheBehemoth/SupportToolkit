namespace SupportToolkit.Core.CustomerMapping;

/// <summary>
/// Lifecycle of an owned Acronis tenant to Business Central customer
/// link.
///
/// Only Confirmed links are used by reconciliation. Rejected pairs are
/// remembered so they are never suggested again. Links are never
/// deleted; an ended link is Retired.
/// </summary>
public enum CustomerMappingState
{
    Suggested,
    Confirmed,
    Rejected,
    Retired
}

/// <summary>
/// How a link was proposed, from strongest to weakest automatic
/// signal. Manual links are created by a person.
/// </summary>
public enum CustomerMatchMethod
{
    Cvr,
    EmailDomain,
    Name,
    Manual
}

/// <summary>
/// One cross-reference record between an Acronis tenant and a Business
/// Central customer.
///
/// This holds real customer relationships at runtime. It belongs in
/// the runtime data store only, never in the repository.
/// </summary>
public sealed record CustomerMappingRecord(
    Guid Id,
    string AcronisTenantId,
    string BcCustomerNo,
    CustomerMappingState State,
    CustomerMatchMethod MatchMethod,
    double? MatchScore = null,
    DateTimeOffset? SuggestedAt = null,
    DateTimeOffset? ConfirmedAt = null,
    string? ConfirmedBy = null,
    DateTimeOffset? RejectedAt = null,
    string? RejectedBy = null,
    DateTimeOffset? RetiredAt = null,
    string? RetiredBy = null,
    string? Notes = null
);

/// <summary>
/// The single definition of allowed state transitions.
/// </summary>
public static class CustomerMappingRules
{
    public static bool IsAllowed(
        CustomerMappingState from,
        CustomerMappingState to)
    {
        return (from, to) switch
        {
            (CustomerMappingState.Suggested, CustomerMappingState.Confirmed) => true,
            (CustomerMappingState.Suggested, CustomerMappingState.Rejected) => true,
            (CustomerMappingState.Confirmed, CustomerMappingState.Retired) => true,
            _ => false
        };
    }

    public static void EnsureAllowed(
        CustomerMappingState from,
        CustomerMappingState to)
    {
        if (!IsAllowed(from, to))
        {
            throw new InvalidOperationException(
                $"A {from} customer mapping cannot become {to}."
            );
        }
    }

    /// <summary>
    /// Identifiers are compared trimmed and case-insensitively, as the
    /// existing tenant selection does.
    /// </summary>
    public static bool SameId(
        string? left,
        string? right)
    {
        return string.Equals(
            left?.Trim(),
            right?.Trim(),
            StringComparison.OrdinalIgnoreCase
        );
    }
}
