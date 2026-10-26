using System.Globalization;

using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.MonitoringScope;

namespace SupportToolkit.Core.Reconciliation;

/// <summary>
/// Classifies Acronis and Business Central evidence into the four
/// reconciliation buckets ("fail closed, quarantine the rest").
///
/// Only Confirmed mappings link the systems. Business Central is the
/// system of record for subscription status and blocked state; Acronis
/// for tenant existence. Undefined or contradictory evidence is
/// Ambiguous, never consistent. Precedence within a mapping is
/// Ambiguous, then MatchedDiscrepancy, then MatchedConsistent.
///
/// The treatment of status Open and blocked Invoice is not decided,
/// so both are Ambiguous. Nothing here creates tickets.
/// </summary>
public sealed class ReconciliationEngine
{
    private readonly SmartSubscriptionStatusVocabulary _vocabulary;

    public ReconciliationEngine(
        SmartSubscriptionStatusVocabulary? vocabulary = null)
    {
        _vocabulary =
            vocabulary
            ?? SmartSubscriptionStatusVocabulary.English;
    }

    /// <param name="relevantItemNumbers">
    /// When supplied, only subscriptions with at least one line for one
    /// of these items are reconciled. When null, every subscription is.
    /// Item numbers are compared trimmed, NFC-normalized and ordinally
    /// ignoring case.
    /// </param>
    /// <param name="excludedTenantIds">
    /// Tenants that are never reconciled or reported, such as the
    /// operator's own tenant.
    /// </param>
    /// <param name="monitoredTenantIds">
    /// Tenants known to be monitored on the Acronis side. When null, no
    /// monitored evidence exists and the monitored-versus-entitlement
    /// checks are skipped; the tenant's enabled state is the only signal.
    /// </param>
    public ReconciliationReport Reconcile(
        IReadOnlyList<CustomerMappingRecord> mappings,
        IReadOnlyList<SmartSubscriptionHeader> headers,
        IReadOnlyList<SmartSubscriptionLine> lines,
        IReadOnlyList<AcronisTenantObservation> tenants,
        DateOnly asOfDate,
        IReadOnlySet<string>? relevantItemNumbers = null,
        IReadOnlySet<string>? excludedTenantIds = null,
        IReadOnlySet<string>? monitoredTenantIds = null)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(tenants);

        var relevantItems =
            relevantItemNumbers is null
                ? null
                : SubscriptionItemNumbers.ToSet(relevantItemNumbers);

        var excluded =
            (excludedTenantIds ?? new HashSet<string>())
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var monitored =
            monitoredTenantIds?
                .Select(id => id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        tenants =
            tenants
                .Where(
                    tenant => !excluded.Contains(tenant.TenantId.Trim()))
                .ToArray();

        mappings =
            mappings
                .Where(
                    mapping => !excluded.Contains(
                        mapping.AcronisTenantId.Trim()))
                .ToArray();

        var period =
            asOfDate.ToString(
                "yyyy-MM",
                CultureInfo.InvariantCulture
            );

        var results =
            new List<ReconciliationResult>();

        var headerNumbers =
            headers
                .Select(header => header.SubscriptionNumber.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Lines for subscriptions the header page does not expose
        // cannot be scoped to a customer.
        foreach (var orphan in lines
            .Where(
                line => !headerNumbers.Contains(
                    line.SubscriptionNumber.Trim()))
            .GroupBy(
                line => line.SubscriptionNumber.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(
                Result(
                    ReconciliationBucket.Unscoped,
                    ReconciliationSubject.BcSubscription,
                    orphan.Key,
                    $"{orphan.Count()} subscription line(s) have no " +
                    "subscription header."
                )
            );
        }

        var inScopeHeaders =
            headers
                .Where(
                    header => IsInScope(
                        header,
                        lines,
                        relevantItems))
                .ToArray();

        var duplicateSubscriptionNumbers =
            inScopeHeaders
                .GroupBy(
                    header => header.SubscriptionNumber.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var header in inScopeHeaders
            .Where(
                header => string.IsNullOrWhiteSpace(
                    header.CustomerNumber)))
        {
            results.Add(
                Result(
                    ReconciliationBucket.Unscoped,
                    ReconciliationSubject.BcSubscription,
                    header.SubscriptionNumber.Trim(),
                    "Subscription header has no customer number."
                )
            );
        }

        var headersByCustomer =
            inScopeHeaders
                .Where(
                    header => !string.IsNullOrWhiteSpace(
                        header.CustomerNumber))
                .GroupBy(
                    header => header.CustomerNumber!.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        var liveTenants =
            tenants
                .GroupBy(
                    tenant => tenant.TenantId.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

        var confirmed =
            mappings
                .Where(
                    mapping =>
                        mapping.State == CustomerMappingState.Confirmed)
                .OrderBy(
                    mapping => mapping.AcronisTenantId,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    mapping => mapping.BcCustomerNo,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var mappedTenants =
            confirmed
                .Select(mapping => mapping.AcronisTenantId.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mappedCustomers =
            confirmed
                .Select(mapping => mapping.BcCustomerNo.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in confirmed)
        {
            results.Add(
                ReconcileMapping(
                    mapping,
                    confirmed,
                    headersByCustomer.GetValueOrDefault(
                        mapping.BcCustomerNo.Trim())
                        ?? [],
                    liveTenants.GetValueOrDefault(
                        mapping.AcronisTenantId.Trim())
                        ?? [],
                    duplicateSubscriptionNumbers,
                    asOfDate,
                    period,
                    monitored?.Contains(mapping.AcronisTenantId.Trim())
                )
            );
        }

        foreach (var tenantId in liveTenants
            .Where(
                pair =>
                    !mappedTenants.Contains(pair.Key)
                    && pair.Value.Any(tenant => !tenant.Deleted))
            .Select(pair => pair.Key)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(
                Result(
                    ReconciliationBucket.Unscoped,
                    ReconciliationSubject.AcronisTenant,
                    tenantId,
                    "Acronis tenant has no confirmed customer mapping."
                )
            );
        }

        foreach (var customer in headersByCustomer.Keys
            .Where(customer => !mappedCustomers.Contains(customer))
            .OrderBy(customer => customer, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(
                Result(
                    ReconciliationBucket.Unscoped,
                    ReconciliationSubject.BcCustomer,
                    customer,
                    "Business Central customer has subscriptions but " +
                    "no confirmed customer mapping."
                )
            );
        }

        return new ReconciliationReport(
            period,
            results
        );
    }

    private ReconciliationResult ReconcileMapping(
        CustomerMappingRecord mapping,
        IReadOnlyList<CustomerMappingRecord> confirmed,
        IReadOnlyList<SmartSubscriptionHeader> customerHeaders,
        IReadOnlyList<AcronisTenantObservation> tenantObservations,
        IReadOnlySet<string> duplicateSubscriptionNumbers,
        DateOnly asOfDate,
        string period,
        bool? monitored)
    {
        var ambiguity =
            new List<string>();

        if (confirmed.Count(
                other => CustomerMappingRules.SameId(
                    other.AcronisTenantId,
                    mapping.AcronisTenantId)) > 1
            || confirmed.Count(
                other => CustomerMappingRules.SameId(
                    other.BcCustomerNo,
                    mapping.BcCustomerNo)) > 1)
        {
            ambiguity.Add(
                "Tenant or customer occurs in more than one confirmed mapping."
            );
        }

        if (tenantObservations.Count > 1)
        {
            ambiguity.Add(
                "Acronis tenant id occurs more than once."
            );
        }

        var tenant =
            tenantObservations.Count == 1
                ? tenantObservations[0]
                : null;

        if (tenant is { Deleted: false, Enabled: null })
        {
            ambiguity.Add(
                "Acronis tenant does not report whether it is enabled."
            );
        }

        if (monitored == true
            && tenant is null)
        {
            ambiguity.Add(
                "Tenant is reported as monitored but was not found."
            );
        }

        // A monitored tenant whose customer has no entitled
        // subscription is decided below; without monitored evidence
        // the case stays undefined.
        var monitoredWithoutEntitlement =
            monitored == true
            && customerHeaders.Count == 0;

        if (customerHeaders.Count == 0
            && !monitoredWithoutEntitlement)
        {
            ambiguity.Add(
                "Confirmed customer has no in-scope subscription " +
                "headers; the treatment of this case is undefined."
            );
        }

        var active =
            new List<string>();

        var inactive =
            new List<string>();

        foreach (var header in customerHeaders)
        {
            var number =
                header.SubscriptionNumber.Trim();

            var concern =
                DescribeAmbiguity(
                    header,
                    duplicateSubscriptionNumbers,
                    asOfDate
                );

            if (concern is not null)
            {
                ambiguity.Add($"{number}: {concern}");
                continue;
            }

            var status =
                SmartSubscriptionStates.ParseStatus(
                    header.Status,
                    _vocabulary
                );

            var blocked =
                SmartSubscriptionStates.ParseBlocked(
                    header.BlockedValue
                );

            if (status == SubscriptionStatusState.Active
                && blocked == SubscriptionBlockedState.NotBlocked)
            {
                active.Add(number);
            }
            else
            {
                inactive.Add(number);
            }
        }

        if (monitored == false
            && active.Count > 0)
        {
            ambiguity.Add(
                "Entitled subscription is active but the tenant is not " +
                "reported as monitored; the treatment of this case is " +
                "undefined."
            );
        }

        if (ambiguity.Count > 0)
        {
            return new ReconciliationResult(
                ReconciliationBucket.Ambiguous,
                ReconciliationSubject.Mapping,
                mapping.Id.ToString(),
                mapping.Id,
                [],
                ambiguity
            );
        }

        var tenantActive =
            tenant is { Deleted: false, Enabled: true };

        var discrepancies =
            new List<ReconciliationDiscrepancy>();

        if (monitoredWithoutEntitlement)
        {
            discrepancies.Add(
                new ReconciliationDiscrepancy(
                    new DiscrepancyKey(
                        mapping.Id,
                        DiscrepancyType.MonitoredWithoutEntitlement,
                        period),
                    [],
                    "Acronis tenant is monitored, but the customer has " +
                    "no in-scope entitled subscription."
                )
            );
        }

        if (tenantActive
            && active.Count == 0
            && inactive.Count > 0)
        {
            discrepancies.Add(
                new ReconciliationDiscrepancy(
                    new DiscrepancyKey(
                        mapping.Id,
                        DiscrepancyType.TenantActiveSubscriptionInactive,
                        period),
                    inactive,
                    "Acronis tenant is enabled, but every in-scope " +
                    "subscription is suspended or blocked."
                )
            );
        }

        if (!tenantActive
            && active.Count > 0)
        {
            discrepancies.Add(
                new ReconciliationDiscrepancy(
                    new DiscrepancyKey(
                        mapping.Id,
                        DiscrepancyType.SubscriptionActiveTenantMissing,
                        period),
                    active,
                    tenant is null
                        ? "Subscription is active, but the Acronis tenant was not found."
                        : "Subscription is active, but the Acronis tenant is disabled or deleted."
                )
            );
        }

        return new ReconciliationResult(
            discrepancies.Count > 0
                ? ReconciliationBucket.MatchedDiscrepancy
                : ReconciliationBucket.MatchedConsistent,
            ReconciliationSubject.Mapping,
            mapping.Id.ToString(),
            mapping.Id,
            discrepancies,
            discrepancies.Count > 0
                ? discrepancies.Select(d => d.Reason).ToArray()
                : ["Business Central and Acronis evidence agree."]
        );
    }

    private string? DescribeAmbiguity(
        SmartSubscriptionHeader header,
        IReadOnlySet<string> duplicateSubscriptionNumbers,
        DateOnly asOfDate)
    {
        if (duplicateSubscriptionNumbers.Contains(
            header.SubscriptionNumber.Trim()))
        {
            return "Subscription number occurs more than once.";
        }

        var status =
            SmartSubscriptionStates.ParseStatus(
                header.Status,
                _vocabulary
            );

        switch (status)
        {
            case SubscriptionStatusState.Open:
                return "Status Open has no agreed treatment.";
            case SubscriptionStatusState.Unrecognized:
                return "Status is missing or unrecognized.";
        }

        switch (SmartSubscriptionStates.ParseBlocked(header.BlockedValue))
        {
            case SubscriptionBlockedState.Invoice:
                return "Blocked state Invoice has no agreed treatment.";
            case SubscriptionBlockedState.Unrecognized:
                return "Blocked state is unrecognized.";
            case SubscriptionBlockedState.NotReported:
                return "Blocked state is not reported.";
        }

        if (status == SubscriptionStatusState.Active
            && header.EndDate.HasValue
            && header.EndDate.Value < asOfDate)
        {
            return "Subscription is active but its end date has passed.";
        }

        return null;
    }

    private static bool IsInScope(
        SmartSubscriptionHeader header,
        IReadOnlyList<SmartSubscriptionLine> lines,
        IReadOnlySet<string>? relevantItemNumbers)
    {
        if (relevantItemNumbers is null)
        {
            return true;
        }

        // Expects a set built by SubscriptionItemNumbers.ToSet.

        var number =
            header.SubscriptionNumber.Trim();

        return lines.Any(
            line =>
                string.Equals(
                    line.SubscriptionNumber.Trim(),
                    number,
                    StringComparison.OrdinalIgnoreCase)
                && relevantItemNumbers.Contains(
                    line.ItemNumber.Trim())
        );
    }

    private static ReconciliationResult Result(
        ReconciliationBucket bucket,
        ReconciliationSubject subject,
        string subjectId,
        string reason)
    {
        return new ReconciliationResult(
            bucket,
            subject,
            subjectId,
            null,
            [],
            [reason]
        );
    }
}
