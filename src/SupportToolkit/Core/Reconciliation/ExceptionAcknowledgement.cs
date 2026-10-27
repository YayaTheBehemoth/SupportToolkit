namespace SupportToolkit.Core.Reconciliation;

/// <summary>
/// A known discrepancy accepted for a limited time.
///
/// It matches one discrepancy type on one Acronis tenant, independent
/// of the reconciliation period, so it does not lapse monthly. While
/// active (up to and including ExpiresOn) the discrepancy is shown as
/// acknowledged and never raised. After expiry it resurfaces on its
/// own; nothing has to be removed.
/// </summary>
public sealed record ExceptionAcknowledgement(
    string AcronisTenantId,
    DiscrepancyType Type,
    string Reason,
    DateOnly ExpiresOn,
    string? AcknowledgedBy = null)
{
    public bool IsActive(
        DateOnly asOfDate) =>
        asOfDate <= ExpiresOn;

    public bool Matches(
        string? acronisTenantId,
        DiscrepancyType type) =>
        Type == type
        && string.Equals(
            AcronisTenantId.Trim(),
            acronisTenantId?.Trim(),
            StringComparison.OrdinalIgnoreCase);
}
