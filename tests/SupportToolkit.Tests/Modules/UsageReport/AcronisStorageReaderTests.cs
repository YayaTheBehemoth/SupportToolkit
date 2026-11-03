using SupportToolkit.Modules.UsageReport.Production;
using SupportToolkit.Providers.Acronis.Usage;

namespace SupportToolkit.Tests.Modules.UsageReport;

/// <summary>
/// The only place that knows how Acronis reports storage: which metrics,
/// how their lines add up and which units are accepted.
/// </summary>
public class AcronisStorageReaderTests
{
    private const string TenantA = "22222222-0000-4000-8000-00000000000a";
    private const string TenantB = "22222222-0000-4000-8000-00000000000b";

    [Fact]
    public void AMetric_IsSummedOverItsLines_OnePerEditionAndLocation()
    {
        var usage =
            Usage(
                (TenantA, "c2c_storage", 0m, "bytes"),
                (TenantA, "c2c_storage", 632_812_228_608m, "bytes"),
                (TenantA, "c2c_storage", 10m, "bytes"));

        var measured =
            AcronisStorageReader.Measure(TenantA, AcronisStorageMetrics.Microsoft365, usage);

        Assert.Equal(632_812_228_618m, measured.Bytes);
    }

    [Fact]
    public void ThePortalSample_ReadsAsThePortalShowsIt()
    {
        // The customer the metrics were confirmed against: backup 540.46,
        // Microsoft 365 589.35 and total 1.10 TB in the portal.
        var usage =
            Usage(
                (TenantA, "storage_total", 1_213_130_870_784m, "bytes"),
                (TenantA, "total_storage", 580_318_642_176m, "bytes"),
                (TenantA, "c2c_storage", 0m, "bytes"),
                (TenantA, "c2c_storage", 632_812_228_608m, "bytes"));

        var storage = AcronisStorageReader.Read([TenantA], usage).ByTenant[TenantA];

        Assert.Equal(540.46m, Math.Round(storage.Backup.PortalGb!.Value, 2));
        Assert.Equal(589.35m, Math.Round(storage.Microsoft365.PortalGb!.Value, 2));
        Assert.Equal(storage.Total.Bytes, storage.Backup.Bytes + storage.Microsoft365.Bytes);
    }

    [Fact]
    public void NoUsageReturned_IsNothingReported_NotZero()
    {
        var measured =
            AcronisStorageReader.Measure(TenantA, AcronisStorageMetrics.Total, Usage());

        Assert.Null(measured.Bytes);
        Assert.Equal("no Acronis usage returned", measured.Problem);
        Assert.True(measured.NotReported);
    }

    [Fact]
    public void AMetricNotReported_IsNothingReported()
    {
        var usage = Usage((TenantA, "total_storage", 5m, "bytes"));

        var measured =
            AcronisStorageReader.Measure(TenantA, AcronisStorageMetrics.Total, usage);

        Assert.Equal("storage_total not reported", measured.Problem);
        Assert.True(measured.NotReported);
    }

    [Fact]
    public void AMissingValue_IsUnknown_AndIsNotNothingReported()
    {
        var usage =
            Usage(
                (TenantA, "c2c_storage", 5m, "bytes"),
                (TenantA, "c2c_storage", (decimal?)null, "bytes"));

        var measured =
            AcronisStorageReader.Measure(TenantA, AcronisStorageMetrics.Microsoft365, usage);

        Assert.Null(measured.Bytes);
        Assert.Contains("missing", measured.Problem);
        Assert.False(measured.NotReported);
    }

    [Fact]
    public void AUnitOtherThanBytes_IsUnknown_NotConverted()
    {
        var usage = Usage((TenantA, "storage_total", 5m, "gigabytes"));

        var measured =
            AcronisStorageReader.Measure(TenantA, AcronisStorageMetrics.Total, usage);

        Assert.Null(measured.Bytes);
        Assert.Contains("not bytes", measured.Problem);
        Assert.False(measured.NotReported);
    }

    [Fact]
    public void Read_GivesEveryRequestedTenant_AnEntry_AndAlwaysTheCaveats()
    {
        var usage = Usage((TenantA, "storage_total", 5m, "bytes"));

        var storage = AcronisStorageReader.Read([TenantA, TenantB], usage);

        Assert.Equal(2, storage.ByTenant.Count);
        Assert.True(storage.ByTenant[TenantB].Total.NotReported);
        Assert.Equal("2026-11-01T00:00:00Z", storage.PeriodStart);
        Assert.Contains(storage.Notices, notice => notice.Contains("not meant for billing"));
        Assert.Contains(storage.Notices, notice => notice.Contains("binary GB"));
    }

    [Fact]
    public void UsageNotAskedFor_IsIgnored_AndSaidSo()
    {
        var storage =
            AcronisStorageReader.Read(
                [TenantA],
                new AcronisUsageResult(Usage().ByTenant, UnrequestedItems: 3));

        Assert.Contains(storage.Notices, notice => notice.Contains("not asked for"));
    }

    private static AcronisUsageResult Usage(
        params (string Tenant, string Metric, decimal? Value, string Unit)[] rows) =>
        new(
            rows
                .GroupBy(row => row.Tenant, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<AcronisTenantUsage>)group
                        .Select(row => new AcronisTenantUsage(
                            row.Tenant, row.Metric, null, null,
                            row.Value, row.Value, row.Unit, "2026-11-01T00:00:00Z"))
                        .ToArray(),
                    StringComparer.OrdinalIgnoreCase),
            0);
}
