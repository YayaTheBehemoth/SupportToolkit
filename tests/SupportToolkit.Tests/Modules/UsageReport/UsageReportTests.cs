using SupportToolkit.Composition;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.UsageReport.Fixtures;
using SupportToolkit.Modules.UsageReport.Models;
using SupportToolkit.Modules.UsageReport.Reporting;
using SupportToolkit.Modules.UsageReport.Services;
using static SupportToolkit.Tests.Ui.Web.StatusTestSupport;
using Report = SupportToolkit.Modules.UsageReport.Models.UsageReport;

namespace SupportToolkit.Tests.Modules.UsageReport;

/// <summary>
/// The usage report puts reported storage next to what is billed. Every
/// figure it cannot establish is unknown with a reason, never zero, and
/// nothing is judged. These tests use only the module's own types: the
/// report does not know which systems the figures came from.
/// </summary>
public class UsageReportTests
{
    private const string Item = "EXAMPLE-BACKUP";
    private const string TenantA = "22222222-0000-4000-8000-00000000000a";
    private const string TenantB = "22222222-0000-4000-8000-00000000000b";

    // ---------------------------------------------------------------
    // Units: the portal sample the metrics were confirmed against.
    // ---------------------------------------------------------------

    [Fact]
    public void Bytes_AreComparedInDecimalGb_AndThePortalFigureIsBinary()
    {
        // Backup storage that the Acronis portal shows as 540.46 GB.
        var measurement = new UsageMeasurement(580_318_642_176m, null);

        Assert.Equal(580.32m, Math.Round(measurement.Gb!.Value, 2));
        Assert.Equal(540.46m, Math.Round(measurement.PortalGb!.Value, 2));
    }

    [Fact]
    public void AnUnknownMeasurement_HasNoGigabytes()
    {
        var unknown = UsageMeasurement.Unknown("why");

        Assert.False(unknown.IsKnown);
        Assert.Null(unknown.Gb);
        Assert.Null(unknown.PortalGb);
    }

    [Fact]
    public void ASum_IsUnknown_WhenAnyPartIs_WithThatPartsReason()
    {
        var sum =
            UsageMeasurement.Sum(
            [
                new UsageMeasurement(5m, null),
                UsageMeasurement.NothingReported("not yet")
            ]);

        Assert.Null(sum.Bytes);
        Assert.Equal("not yet", sum.Problem);
        Assert.True(sum.NotReported);

        Assert.Equal(
            15m,
            UsageMeasurement.Sum([new UsageMeasurement(5m, null), new UsageMeasurement(10m, null)]).Bytes);
    }

    // ---------------------------------------------------------------
    // Billing lines
    // ---------------------------------------------------------------

    [Fact]
    public void ACustomer_IsComparedOnItsActiveBackupLines()
    {
        var report =
            Build(
                headers: [Header("S-1", "C-1")],
                lines:
                [
                    Line("S-1", 10000, 300m, 30m),
                    Line("S-1", 20000, 200m, 20m),
                    Line("S-1", 30000, 999m, 99m, item: "SOMETHING-ELSE")
                ],
                storage: Storage((TenantA, Figures(450e9m, 250e9m, 200e9m))),
                links: [("C-1", TenantA)]);

        var row = Assert.Single(report.Customers);

        Assert.True(row.IsCompared);
        Assert.Equal(500m, row.BilledQuantity);
        Assert.Equal(50m, row.BilledAmount);
        Assert.Equal(450m, row.Total.Gb);
        Assert.Equal(50m, row.DifferenceGb);
        Assert.Empty(row.Remarks);
        Assert.Equal(2, row.Lines.Count);
    }

    [Fact]
    public void AnOmittedLine_IsNotCounted_ButIsShown()
    {
        var report =
            Build(
                headers: [Header("S-1", "C-1")],
                lines:
                [
                    Line("S-1", 10000, 300m, 30m),
                    Line("S-1", 20000, 200m, 20m, omit: true)
                ],
                storage: Storage((TenantA, Figures(300e9m, 300e9m, 0m))),
                links: [("C-1", TenantA)]);

        var row = Assert.Single(report.Customers);

        Assert.Equal(300m, row.BilledQuantity);
        Assert.Equal(1, row.ExcludedLines);
        Assert.Contains(row.Remarks, remark => remark.Contains("not counted"));
    }

    [Fact]
    public void ACustomerWithOnlyASuspendedSubscription_IsLeftOut_AndListedApart()
    {
        var report =
            Build(
                headers: [Header("S-1", "C-1", status: "Suspended")],
                lines: [Line("S-1", 10000, 300m, 30m)],
                storage: Storage((TenantA, Figures(40e9m, 40e9m, 0m))),
                links: [("C-1", TenantA)]);

        Assert.Empty(report.Customers);

        var row = Assert.Single(report.NotActive);

        Assert.Null(row.BilledQuantity);
        Assert.Null(row.DifferenceGb);

        // Storage on a subscription that is not active is still shown.
        Assert.Equal(40m, row.Total.Gb);

        // Not a data problem, so the report is not incomplete because of it.
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void AMissingQuantityOrAmount_IsUnknown_NotZero()
    {
        var report =
            Build(
                headers: [Header("S-1", "C-1")],
                lines: [Line("S-1", 10000, null, null)],
                storage: Storage((TenantA, Figures(1m, 1m, 0m))),
                links: [("C-1", TenantA)]);

        var row = Assert.Single(report.Customers);

        Assert.Null(row.BilledQuantity);
        Assert.Null(row.BilledAmount);
        Assert.Null(row.DifferenceGb);
        Assert.Contains("a line quantity is missing", row.Problems);
        Assert.Contains("a line amount is missing", row.Problems);
    }

    [Fact]
    public void ALineWhoseSubscriptionIsUnknown_IsCounted_NotGuessed()
    {
        var report =
            Build(
                headers: [Header("S-1", "C-1")],
                lines:
                [
                    Line("S-1", 10000, 100m, 10m),
                    Line("S-404", 10000, 100m, 10m)
                ],
                storage: Storage((TenantA, Figures(100e9m, 100e9m, 0m))),
                links: [("C-1", TenantA)]);

        Assert.Equal(1, report.UnattributedLines);
        Assert.Equal(100m, Assert.Single(report.Customers).BilledQuantity);
        Assert.False(report.IsComplete);
    }

    // ---------------------------------------------------------------
    // Storage and matching
    // ---------------------------------------------------------------

    [Fact]
    public void SeveralTenants_OfOneCustomer_AreAddedUp()
    {
        var report =
            Build(
                headers: [Header("S-1", "C-1")],
                lines: [Line("S-1", 10000, 150m, 15m)],
                storage: Storage(
                    (TenantA, Figures(100e9m, 100e9m, 0m)),
                    (TenantB, Figures(50e9m, 20e9m, 30e9m))),
                links: [("C-1", TenantA), ("C-1", TenantB)]);

        var row = Assert.Single(report.Customers);

        Assert.Equal(150m, row.Total.Gb);
        Assert.Equal(120m, row.Backup.Gb);
        Assert.Equal(30m, row.Microsoft365.Gb);
        Assert.Equal(0m, row.DifferenceGb);
    }

    [Fact]
    public void ACustomerWithoutATenant_IsUnknown()
    {
        var report =
            Build(
                headers: [Header("S-1", "C-1")],
                lines: [Line("S-1", 10000, 100m, 10m)],
                storage: Storage(),
                links: []);

        var row = Assert.Single(report.Customers);

        Assert.Contains("no tenant matched", row.Problems);
        Assert.Null(row.Total.Bytes);
    }

    [Fact]
    public void ACustomerSetUpButWithoutUsageYet_IsListedApart_NotUnknown()
    {
        var report =
            Build(
                headers: [Header("S-1", "C-1")],
                lines: [Line("S-1", 10000, 50m, 5m)],
                storage: Storage(),
                links: [("C-1", TenantA)]);

        Assert.Empty(report.Customers);

        var row = Assert.Single(report.AwaitingUsage);

        Assert.Equal(50m, row.BilledQuantity);
        Assert.Null(row.Total.Bytes);
        Assert.Null(row.DifferenceGb);
        Assert.Contains(row.Remarks, remark => remark.Contains("no usage has been reported yet"));
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void UnusableUsage_IsStillAProblem_NotNoUsageYet()
    {
        var unusable = UsageMeasurement.Unknown("unit is gigabytes, not bytes");

        var report =
            Build(
                headers: [Header("S-1", "C-1")],
                lines: [Line("S-1", 10000, 50m, 5m)],
                storage: Storage((TenantA, new TenantStorage(unusable, unusable, unusable))),
                links: [("C-1", TenantA)]);

        var row = Assert.Single(report.Customers);

        Assert.False(row.IsKnown);
        Assert.Empty(report.AwaitingUsage);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public void APendingMatch_IsReported_NeverUsed()
    {
        var report =
            Build(
                headers: [Header("S-1", "C-1")],
                lines: [Line("S-1", 10000, 100m, 10m)],
                storage: Storage((TenantA, Figures(100e9m, 100e9m, 0m))),
                links: [],
                pending: [("C-1", TenantA)]);

        var row = Assert.Single(report.Customers);

        Assert.Contains("the match to a tenant needs review", row.Problems);
        Assert.Equal(1, report.PendingMatches);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public void ATenantWithStorage_ButNoBackupSubscription_IsListed()
    {
        var report =
            Build(
                headers: [],
                lines: [],
                storage: Storage((TenantA, Figures(75e9m, 50e9m, 25e9m))),
                links: []);

        var row = Assert.Single(report.TenantsWithoutSubscription);

        Assert.Equal(75m, row.Total.Gb);
        Assert.True(row.IsKnown);
    }

    [Fact]
    public void ATenantWithoutAnyStorage_IsASpecialCase()
    {
        var report =
            Build(
                headers: [],
                lines: [],
                storage: Storage((TenantA, Figures(0m, 0m, 0m))),
                links: []);

        var row = Assert.Single(report.SpecialCases);

        Assert.Contains(row.Remarks, remark => remark.Contains("no storage"));
        Assert.Empty(report.TenantsWithoutSubscription);
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void ATenantWithoutSubscription_AndWithoutUsage_IsASpecialCase()
    {
        var report =
            Build(
                headers: [],
                lines: [],
                storage: Storage(),
                links: [],
                extraTenants: [TenantA]);

        var row = Assert.Single(report.SpecialCases);

        Assert.Contains(row.Remarks, remark => remark.Contains("no usage reported yet"));
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void ATotalThatIsNotBackupPlusMicrosoft365_IsFlagged_ButStillCompared()
    {
        var report =
            Build(
                headers: [Header("S-1", "C-1")],
                lines: [Line("S-1", 10000, 240m, 24m)],
                storage: Storage((TenantA, Figures(240e9m, 100e9m, 130e9m))),
                links: [("C-1", TenantA)]);

        var row = Assert.Single(report.Customers);

        Assert.True(row.IsCompared);
        Assert.Contains(row.Remarks, remark => remark.Contains("not backup + Microsoft 365"));
    }

    [Fact]
    public void TheReport_SaysWhichUnitIsUsed_AndPassesOnTheSourcesNotes()
    {
        var report =
            Build(
                [], [], Storage() with { Notices = ["A note from the storage source."] }, []);

        Assert.Contains(report.Notices, notice => notice.Contains("decimal GB"));
        Assert.Contains("A note from the storage source.", report.Notices);
    }

    // ---------------------------------------------------------------
    // Workflow, availability and session
    // ---------------------------------------------------------------

    [Fact]
    public async Task FixtureMode_ShowsEveryKindOfRow_AndIsIncomplete()
    {
        var workflow = UsageReportComposition.Workflow(Resolver(SupportToolkitMode.Fixture));

        var report = await workflow.GenerateAsync();

        var byName = report.Customers.ToDictionary(row => row.Name);

        Assert.Equal(0m, byName["Fjeldsted Example Revision"].DifferenceGb);
        Assert.Equal(-20m, byName["Nordvik Example Logistik"].DifferenceGb);
        Assert.Contains("a line amount is missing", byName["Ravnsborg Example Advokater"].Problems);
        Assert.Contains("no tenant matched", byName["Kildebakke Example Byg"].Problems);
        Assert.Equal(4, byName.Count);

        Assert.Equal("Saltholm Example Marine", Assert.Single(report.NotActive).Name);
        Assert.Equal("Bakkegaard Example Tandlaeger", Assert.Single(report.AwaitingUsage).Name);
        Assert.Equal("Hvidbjerg Example Consulting", Assert.Single(report.TenantsWithoutSubscription).Name);
        Assert.Equal(SupportToolkitMode.Fixture, report.Mode);
        Assert.Equal("Tranum Example Holding", Assert.Single(report.SpecialCases).Name);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public async Task FixtureMode_IsAlwaysAvailable()
    {
        var availability =
            await UsageReportComposition.Workflow(Resolver(SupportToolkitMode.Fixture))
                .GetAvailabilityAsync();

        Assert.True(availability.IsAvailable);
    }

    [Fact]
    public async Task Production_WithoutBackupItems_IsUnavailable_AndReadsNothing()
    {
        var read = false;

        var workflow =
            UsageReportComposition.Workflow(
                Resolver(SupportToolkitMode.Production, items: []),
                productionSources: _ =>
                {
                    read = true;
                    return Task.FromResult<IUsageReportSources>(new FixtureUsageReportSources());
                });

        var availability = await workflow.GetAvailabilityAsync();

        Assert.False(availability.IsAvailable);
        Assert.Contains("No backup items", availability.Reason);

        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.GenerateAsync());
        Assert.False(read);
    }

    [Fact]
    public async Task Production_UsesTheItemsConfiguredForTheUsageReport()
    {
        // The real systems are replaced by synthetic ones; nothing is contacted.
        var workflow =
            UsageReportComposition.Workflow(
                Resolver(SupportToolkitMode.Production, items: [FixtureUsageReportSources.BackupItem]),
                productionSources: _ =>
                    Task.FromResult<IUsageReportSources>(new FixtureUsageReportSources()));

        Assert.True((await workflow.GetAvailabilityAsync()).IsAvailable);

        var report = await workflow.GenerateAsync();

        Assert.Equal(SupportToolkitMode.Production, report.Mode);
        Assert.Equal(4, report.Customers.Count());
        Assert.Single(report.NotActive);
        Assert.Single(report.AwaitingUsage);

        // Another item configured: none of these lines are backup lines.
        var other =
            await UsageReportComposition.Workflow(
                    Resolver(SupportToolkitMode.Production, items: ["NOT-A-FIXTURE-ITEM"]),
                    productionSources: _ =>
                        Task.FromResult<IUsageReportSources>(new FixtureUsageReportSources()))
                .GenerateAsync();

        Assert.Empty(other.Customers);
    }

    [Fact]
    public async Task AFailedRun_LeavesNoCurrentReport()
    {
        var session =
            new UsageReportSession(
                UsageReportComposition.Workflow(
                    Resolver(SupportToolkitMode.Production, items: [Item]),
                    productionSources: _ => throw new InvalidOperationException("down")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GenerateAsync());

        Assert.Null(session.CurrentReport);
    }

    [Fact]
    public async Task TheSession_HoldsTheLastReport()
    {
        var session =
            new UsageReportSession(UsageReportComposition.Workflow(Resolver(SupportToolkitMode.Fixture)));

        var report = await session.GenerateAsync();

        Assert.Same(report, session.CurrentReport);
    }

    // ---------------------------------------------------------------
    // Console output
    // ---------------------------------------------------------------

    [Fact]
    public async Task TheConsole_HidesNamesAndAmounts_UnlessAskedFor()
    {
        var report =
            await UsageReportComposition.Workflow(Resolver(SupportToolkitMode.Fixture)).GenerateAsync();

        var hidden = new StringWriter();
        ConsoleUsageReportReporter.Write(hidden, report, showNames: false);

        Assert.DoesNotContain("Fjeldsted", hidden.ToString());
        Assert.Contains("INCOMPLETE", hidden.ToString());
        Assert.Contains("--names", hidden.ToString());

        var shown = new StringWriter();
        ConsoleUsageReportReporter.Write(shown, report, showNames: true);

        Assert.Contains("Fjeldsted Example Revision", shown.ToString());
        Assert.Contains("-20.00", shown.ToString());
        Assert.Contains("unknown", shown.ToString());
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------

    private static SupportToolkitConfigurationResolver Resolver(
        SupportToolkitMode mode,
        IReadOnlyList<string>? items = null)
    {
        var configuration = FullConfiguration(SupportToolkitMode.Fixture);
        var profile = configuration.Profiles[configuration.ActiveProfile!];

        profile.Modules["usage-report"] = mode;
        profile.MonitoringScope["usage-report"] =
            new ModuleMonitoringScopeConfiguration
            {
                ServiceItemNumbers = (items ?? [Item]).ToList()
            };

        var (_, _, resolver) = Create(configuration, FullSecrets());

        return resolver;
    }

    private static SmartSubscriptionHeader Header(
        string number,
        string customer,
        string status = "Active") =>
        new(number, customer, status, new DateOnly(2025, 1, 1), null);

    private static SubscriptionBillingLine Line(
        string subscription,
        int lineNumber,
        decimal? quantity,
        decimal? amount,
        string item = Item,
        bool omit = false) =>
        new(subscription, lineNumber, item, "Example backup storage", "GB",
            quantity, 0.1m, 0m, amount, omit);

    private static TenantStorage Figures(
        decimal total,
        decimal backup,
        decimal microsoft365) =>
        new(
            new UsageMeasurement(total, null),
            new UsageMeasurement(backup, null),
            new UsageMeasurement(microsoft365, null));

    private static UsageStorage Storage(
        params (string Tenant, TenantStorage Figures)[] tenants) =>
        new(
            tenants.ToDictionary(
                tenant => tenant.Tenant,
                tenant => tenant.Figures,
                StringComparer.OrdinalIgnoreCase),
            "2026-11-01T00:00:00Z",
            []);

    private static Report Build(
        IReadOnlyList<SmartSubscriptionHeader> headers,
        IReadOnlyList<SubscriptionBillingLine> lines,
        UsageStorage storage,
        IReadOnlyList<(string Customer, string Tenant)> links,
        IReadOnlyList<(string Customer, string Tenant)>? pending = null,
        IReadOnlyList<string>? extraTenants = null)
    {
        var tenantIds =
            storage.ByTenant.Keys
                .Concat(links.Select(link => link.Tenant))
                .Concat((pending ?? []).Select(link => link.Tenant))
                .Concat(extraTenants ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var customers =
            headers
                .Where(header => header.CustomerNumber is not null)
                .Select(header => new CustomerParty(header.CustomerNumber!, "Customer " + header.CustomerNumber))
                .ToArray();

        static CustomerMappingRecord Record(
            (string Customer, string Tenant) link,
            CustomerMappingState state) =>
            new(Guid.NewGuid(), link.Tenant, link.Customer, state, CustomerMatchMethod.Name);

        return UsageReportBuilder.Build(
            new UsageReportInputs(
                headers,
                lines,
                [Item],
                new ScopedCustomerParties(customers, 0, 0),
                new UsageTenants(
                    tenantIds.Select(id => new CustomerParty(id, "Tenant " + id[^1])).ToArray(),
                    null),
                new AutoMappingResult(
                    links.Select(link => Record(link, CustomerMappingState.Confirmed)).ToArray(),
                    (pending ?? []).Select(link => Record(link, CustomerMappingState.Suggested)).ToArray(),
                    [],
                    [],
                    []),
                storage),
            new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero),
            SupportToolkitMode.Fixture);
    }
}
