using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Modules.BackupAggregator.Reporting;

/// <summary>
/// Summarizes how a scoped review was selected and whether either the
/// scope or the inventory is complete. The standard inventory report
/// follows separately.
///
/// Scope completeness and inventory completeness are reported
/// separately and never substituted for one another.
/// </summary>
public sealed class ConsoleScopedReviewReporter
{
    private readonly bool _detailed;

    /// <param name="detailed">
    /// Whether the scope report lists every detail section. The
    /// default view is the summary and the tenants in scope.
    /// </param>
    public ConsoleScopedReviewReporter(
        bool detailed = false)
    {
        _detailed = detailed;
    }

    public void Write(
        TextWriter output,
        ScopedBackupInventoryReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(result);

        var selection = result.Selection;

        if (result.ScopeReport is not null)
        {
            new ConsoleMonitoringScopeReporter()
                .Write(output, result.ScopeReport, _detailed);

            WriteCompleteness(output, result);
            return;
        }

        output.WriteLine();
        output.WriteLine("MONITORING SCOPE");
        output.WriteLine("----------------");
        output.WriteLine(
            $"Acronis customers: {selection.Tenants.Count}");
        output.WriteLine(
            $"Selected for review: {selection.InventoryCandidates.Count}");
        output.WriteLine(
            $"Need scope review: {selection.NeedsReviewCount}");
        output.WriteLine(
            $"Unclassified: {selection.UnclassifiedCount}");
        output.WriteLine(
            "Unmatched subscriptions: " +
            selection.UnmatchedEvidence.Count);
        output.WriteLine(
            $"Excluded tenants: {selection.ExcludedTenantCount}");

        var unresolved =
            selection.Tenants
                .Where(
                    finding =>
                        finding.Status
                        != MonitoringTenantSelectionStatus
                            .CandidateForInventory)
                .ToList();

        if (unresolved.Count > 0
            || selection.UnmatchedEvidence.Count > 0
            || selection.Notices.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("UNRESOLVED SCOPE");
            output.WriteLine("----------------");

            foreach (var notice in selection.Notices)
            {
                output.WriteLine($"  Notice: {notice}");
            }

            foreach (var finding in unresolved)
            {
                output.WriteLine(
                    $"  {finding.Tenant.Name} | {finding.Status} | " +
                    finding.Reason);
            }

            foreach (var observation in selection.UnmatchedEvidence)
            {
                output.WriteLine(
                    "  Unmatched subscription: " +
                    observation.SubscriptionNumber);
            }
        }

        WriteCompleteness(output, result);
    }

    private static void WriteCompleteness(
        TextWriter output,
        ScopedBackupInventoryReviewResult result)
    {
        output.WriteLine();
        output.WriteLine("COMPLETENESS");
        output.WriteLine("------------");
        output.WriteLine(
            "Selected inventory complete: " +
            YesNo(result.IsInventoryComplete));
        output.WriteLine(
            "Monitoring scope complete:   " +
            YesNo(result.IsScopeComplete));
        output.WriteLine(
            "Overall complete:            " +
            YesNo(result.IsOverallComplete));

        output.WriteLine();
        output.WriteLine(
            "Only selected tenants were reviewed. The unscoped " +
            "review and ticketing are unchanged;");
        output.WriteLine(
            result.IsOverallComplete
                ? "this review is complete, so a ticket can be " +
                  "previewed and created from it."
                : "a ticket can be created only when the overall " +
                  "review is complete.");
    }

    private static string YesNo(
        bool value)
    {
        return value ? "YES" : "NO";
    }
}
