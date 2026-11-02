using SupportToolkit.Cli.Commands;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Usage;

namespace SupportToolkit.Tests.Cli;

public class UsageCompareDevCommandTests
{
    private const string Alpha = "11111111-1111-1111-1111-111111111111";
    private const string Beta = "22222222-2222-2222-2222-222222222222";
    private const string Gamma = "33333333-3333-3333-3333-333333333333";

    private const decimal Gib = 1_073_741_824m;

    private static readonly string[] Items = ["EXAMPLE-BACKUP", "EXAMPLE-EXTRA"];

    [Theory]
    [InlineData("--production")]
    [InlineData("--production", "--metric", "storage")]
    [InlineData("--production", "--names")]
    public void Arguments_AcceptTheDocumentedShapes(params string[] args)
    {
        Assert.True(
            UsageCompareDevCommand.TryParseArguments(
                args,
                out var options,
                out _));

        Assert.NotNull(options);
    }

    [Fact]
    public void Arguments_DefaultToStorageTotal()
    {
        UsageCompareDevCommand.TryParseArguments(
            ["--production"],
            out var options,
            out _);

        Assert.Equal("storage_total", options!.Metric);
    }

    [Theory]
    [InlineData]
    [InlineData("--names")]
    [InlineData("--production", "--out", "compare.csv")]
    [InlineData("--production", "--names", "--out", "compare.csv")]
    [InlineData("--production", "--metric")]
    [InlineData("--production", "--metric", "a", "--metric", "b")]
    [InlineData("--production", "--unknown")]
    public void Arguments_RejectEverythingElse(params string[] args)
    {
        Assert.False(
            UsageCompareDevCommand.TryParseArguments(
                args,
                out var options,
                out var error));

        Assert.Null(options);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Row_ShowsBothSidesAndTheDifferenceInBothConventions()
    {
        var rows =
            Build(
                links: [new(Alpha, "C-1")],
                evidence: Evidence(Active("S-1", "C-1", "EXAMPLE-BACKUP", 1100m)),
                usage: Usage(Alpha, 1132m * Gib));

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.True(row.IsCompared);
        Assert.Equal(1100m, row.BcQuantity);
        Assert.Equal(1132m, row.AcronisGib);
        Assert.Equal(-32m, row.DiffGib);

        // Decimal GB is a different number for the same bytes.
        Assert.True(row.AcronisGb > row.AcronisGib);
        Assert.NotEqual(row.DiffGib, row.DiffGb);

        Assert.Equal(-32m / 1132m * 100m, row.DiffPercentGib);
        Assert.Equal("EXAMPLE-BACKUP:1100", row.BcItems);
    }

    [Fact]
    public void ConfiguredItemsAreSummed_AndOtherItemsAreIgnored()
    {
        var rows =
            Build(
                links: [new(Alpha, "C-1")],
                evidence: Evidence(
                    Active("S-1", "C-1", "EXAMPLE-BACKUP", 1000m),
                    Active("S-1", "C-1", "EXAMPLE-EXTRA", 100m),
                    // The monitoring item is not a compared item.
                    Active("S-1", "C-1", "EXAMPLE-MONITORING", 7m)),
                usage: Usage(Alpha, 1000m * Gib));

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.Equal(1100m, row.BcQuantity);
        Assert.Equal("EXAMPLE-BACKUP:1000;EXAMPLE-EXTRA:100", row.BcItems);
    }

    [Fact]
    public void InactiveOrOmittedLines_AreExcludedAndCounted_NotSilentlyDropped()
    {
        var rows =
            Build(
                links: [new(Alpha, "C-1")],
                evidence: new SmartSubscriptionEvidence(
                    [
                        Header("S-1", "C-1", "Active"),
                        Header("S-2", "C-1", "Suspended")
                    ],
                    [
                        Line("S-1", "EXAMPLE-BACKUP", 500m),
                        Line("S-1", "EXAMPLE-EXTRA", 50m, omit: true),
                        Line("S-2", "EXAMPLE-BACKUP", 900m)
                    ]),
                usage: Usage(Alpha, 500m * Gib));

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.Equal(500m, row.BcQuantity);
        Assert.Equal(2, row.BcLinesExcluded);
        Assert.Equal(0m, row.DiffGib);
    }

    [Fact]
    public void NoActiveLine_IsUnknown_NeverZero()
    {
        var rows =
            Build(
                links: [new(Alpha, "C-1")],
                evidence: new SmartSubscriptionEvidence(
                    [Header("S-1", "C-1", "Suspended")],
                    [Line("S-1", "EXAMPLE-BACKUP", 500m)]),
                usage: Usage(Alpha, 500m * Gib));

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.Null(row.BcQuantity);
        Assert.Null(row.DiffGib);
        Assert.False(row.IsCompared);
        Assert.Contains("no active line", row.Status);
    }

    [Fact]
    public void MissingQuantity_IsUnknown()
    {
        var rows =
            Build(
                links: [new(Alpha, "C-1")],
                evidence: Evidence(Active("S-1", "C-1", "EXAMPLE-BACKUP", null)),
                usage: Usage(Alpha, 500m * Gib));

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.Null(row.BcQuantity);
        Assert.Contains("quantity is missing", row.Status);
    }

    [Fact]
    public void SeveralMetricMeasurements_AreAmbiguous_AndNotSummed()
    {
        var usage =
            new AcronisUsageResult(
                new Dictionary<string, IReadOnlyList<AcronisTenantUsage>>
                {
                    [Alpha] =
                    [
                        Measure(Alpha, "storage_total", "ed-a", 100m * Gib, "bytes"),
                        Measure(Alpha, "storage_total", "ed-b", 200m * Gib, "bytes")
                    ]
                },
                0);

        var rows =
            Build(
                links: [new(Alpha, "C-1")],
                evidence: Evidence(Active("S-1", "C-1", "EXAMPLE-BACKUP", 300m)),
                usage: usage);

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.Null(row.AcronisBytes);
        Assert.Null(row.DiffGib);
        Assert.Contains("2 storage_total measurements", row.Status);
        Assert.Contains("ed-a", row.Status);
    }

    [Fact]
    public void NonByteUnit_IsUnknown_NotAssumedBytes()
    {
        var usage =
            new AcronisUsageResult(
                new Dictionary<string, IReadOnlyList<AcronisTenantUsage>>
                {
                    [Alpha] =
                    [
                        Measure(Alpha, "storage_total", "ed", 5m, "quantity")
                    ]
                },
                0);

        var rows =
            Build(
                links: [new(Alpha, "C-1")],
                evidence: Evidence(Active("S-1", "C-1", "EXAMPLE-BACKUP", 5m)),
                usage: usage);

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.Null(row.AcronisBytes);
        Assert.Contains("not bytes", row.Status);
    }

    [Fact]
    public void MetricNotReported_IsUnknown()
    {
        var rows =
            Build(
                links: [new(Alpha, "C-1")],
                evidence: Evidence(Active("S-1", "C-1", "EXAMPLE-BACKUP", 5m)),
                usage: new AcronisUsageResult(
                    new Dictionary<string, IReadOnlyList<AcronisTenantUsage>>
                    {
                        [Alpha] =
                        [
                            Measure(Alpha, "other", "ed", 1m, "bytes")
                        ]
                    },
                    0));

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.Contains("storage_total not reported", row.Status);
    }

    [Fact]
    public void ZeroUsage_IsAValueNotUnknown()
    {
        var rows =
            Build(
                links: [new(Alpha, "C-1")],
                evidence: Evidence(Active("S-1", "C-1", "EXAMPLE-BACKUP", 10m)),
                usage: Usage(Alpha, 0m));

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.True(row.IsCompared);
        Assert.Equal(0m, row.AcronisBytes);
        Assert.Equal(10m, row.DiffGib);

        // A percentage of zero is undefined, not infinity.
        Assert.Null(row.DiffPercentGib);
    }

    [Fact]
    public void CustomerWithItemsButNoLinkedTenant_GetsAnExplicitUnknownRow()
    {
        var rows =
            Build(
                links: [],
                evidence: Evidence(Active("S-1", "C-9", "EXAMPLE-BACKUP", 100m)),
                usage: Usage(Alpha, 1m));

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-9");

        Assert.Equal(100m, row.BcQuantity);
        Assert.Null(row.AcronisBytes);
        Assert.Contains("no Acronis tenant linked", row.Status);
    }

    [Fact]
    public void CustomerTenantWithoutALink_StillShowsItsAcronisUsage()
    {
        var rows =
            Build(
                links: [],
                evidence: Evidence(),
                usage: Usage(Alpha, 50m * Gib));

        var row = Assert.Single(rows, r => r.TenantIds == Alpha);

        Assert.Null(row.CustomerNumber);
        Assert.Equal(50m, row.AcronisGib);
        Assert.Contains("not linked to a BC customer", row.Status);

        // The partner tenant is not a customer and has no row.
        Assert.DoesNotContain(rows, r => r.TenantIds == "partner");
    }

    [Fact]
    public void SeveralTenantsOfOneCustomer_AreSummedAgainstOneBcQuantity()
    {
        var usage =
            new AcronisUsageResult(
                new Dictionary<string, IReadOnlyList<AcronisTenantUsage>>
                {
                    [Alpha] = [Measure(Alpha, "storage_total", "ed", 100m * Gib, "bytes")],
                    [Beta] = [Measure(Beta, "storage_total", "ed", 50m * Gib, "bytes")]
                },
                0);

        var rows =
            Build(
                links: [new(Alpha, "C-1"), new(Beta, "C-1")],
                evidence: Evidence(Active("S-1", "C-1", "EXAMPLE-BACKUP", 150m)),
                usage: usage);

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.Equal(150m, row.AcronisGib);
        Assert.Equal(0m, row.DiffGib);
        Assert.Contains(Alpha, row.TenantIds);
        Assert.Contains(Beta, row.TenantIds);
    }

    [Fact]
    public void OneUnknownTenant_MakesTheWholeCustomerUnknown()
    {
        var rows =
            Build(
                links: [new(Alpha, "C-1"), new(Gamma, "C-1")],
                evidence: Evidence(Active("S-1", "C-1", "EXAMPLE-BACKUP", 150m)),
                usage: Usage(Alpha, 100m * Gib));

        var row = Assert.Single(rows, r => r.CustomerNumber == "C-1");

        Assert.Null(row.AcronisBytes);
        Assert.Null(row.DiffGib);
    }

    [Fact]
    public void DefaultReport_HidesNamesAndNumbers_AndSummarisesTheSpread()
    {
        var rows =
            Build(
                links: [new(Alpha, "C-1"), new(Beta, "C-2")],
                evidence: Evidence(
                    Active("S-1", "C-1", "EXAMPLE-BACKUP", 1100m),
                    Active("S-2", "C-2", "EXAMPLE-BACKUP", 520m)),
                usage: new AcronisUsageResult(
                    new Dictionary<string, IReadOnlyList<AcronisTenantUsage>>
                    {
                        [Alpha] = [Measure(Alpha, "storage_total", "ed", 1132m * Gib, "bytes")],
                        [Beta] = [Measure(Beta, "storage_total", "ed", 500m * Gib, "bytes")]
                    },
                    0));

        var output = new StringWriter();

        var exit =
            UsageCompareDevCommand.WriteReport(
                output,
                rows,
                Items,
                "storage_total",
                0,
                showNames: false);

        var text = output.ToString();

        // The third customer tenant is unlinked and has no usage, so
        // the run is incomplete even though two customers compared.
        Assert.Equal(2, exit);
        Assert.DoesNotContain("Alpha Example", text);
        Assert.DoesNotContain("C-1", text);
        Assert.Contains("Compared:           2", text);
        Assert.Contains("min -32, median -6, p90 20, max 20", text);
        Assert.Contains("BC below Acronis (GiB): 1", text);
        Assert.Contains("BC above: 1", text);
    }

    [Fact]
    public void UnknownRows_MakeTheRunIncomplete_WithReasonsCounted()
    {
        var rows =
            Build(
                links: [],
                evidence: Evidence(Active("S-1", "C-9", "EXAMPLE-BACKUP", 100m)),
                usage: Usage(Alpha, 1m));

        var output = new StringWriter();

        var exit =
            UsageCompareDevCommand.WriteReport(
                output,
                rows,
                Items,
                "storage_total",
                3,
                showNames: false);

        var text = output.ToString();

        Assert.Equal(2, exit);
        Assert.Contains("no Acronis tenant linked", text);
        Assert.Contains("awaiting review (not used): 3", text);
    }

    [Fact]
    public void NamesMode_PrintsATableToTheConsole()
    {
        var rows =
            Build(
                links: [new(Alpha, "C-1")],
                evidence: Evidence(Active("S-1", "C-1", "EXAMPLE-BACKUP", 1100m)),
                usage: Usage(Alpha, 1132m * Gib));

        var table = new StringWriter();

        UsageCompareDevCommand.WriteReport(
            table,
            rows,
            Items,
            "storage_total",
            0,
            showNames: true);

        Assert.Contains("Alpha Example", table.ToString());
        Assert.Contains("C-1", table.ToString());
        Assert.Contains("1132", table.ToString());
        Assert.Contains("-32", table.ToString());
    }

    private static IReadOnlyList<UsageComparisonRow> Build(
        IReadOnlyList<TenantLink> links,
        SmartSubscriptionEvidence evidence,
        AcronisUsageResult usage) =>
        UsageComparison.Build(
            Tenants(),
            usage,
            links,
            evidence,
            Items,
            "storage_total");

    private static TenantDto[] Tenants() =>
    [
        new() { Id = "partner", Name = "Example Partner", Kind = "partner" },
        new() { Id = Alpha, Name = "Alpha Example", Kind = "customer" },
        new() { Id = Beta, Name = "Beta Example", Kind = "customer" },
        new() { Id = Gamma, Name = "Gamma Example", Kind = "customer" }
    ];

    private static AcronisUsageResult Usage(
        string tenantId,
        decimal bytes) =>
        new(
            new Dictionary<string, IReadOnlyList<AcronisTenantUsage>>
            {
                [tenantId] =
                [
                    Measure(tenantId, "storage_total", "ed", bytes, "bytes")
                ]
            },
            0);

    private static AcronisTenantUsage Measure(
        string tenantId,
        string metric,
        string edition,
        decimal value,
        string unit) =>
        new(tenantId, metric, edition, "infra", value, value, unit, null);

    private static SmartSubscriptionHeader Header(
        string subscription,
        string customer,
        string status) =>
        new(subscription, customer, status, null, null);

    private static SmartSubscriptionLine Line(
        string subscription,
        string item,
        decimal? quantity,
        bool omit = false) =>
        new(subscription, item, "Item", omit, quantity);

    private static (SmartSubscriptionHeader, SmartSubscriptionLine) Active(
        string subscription,
        string customer,
        string item,
        decimal? quantity) =>
        (Header(subscription, customer, "Active"),
         Line(subscription, item, quantity));

    private static SmartSubscriptionEvidence Evidence(
        params (SmartSubscriptionHeader Header, SmartSubscriptionLine Line)[] pairs) =>
        new(
            pairs.Select(pair => pair.Header)
                .DistinctBy(header => header.SubscriptionNumber)
                .ToArray(),
            pairs.Select(pair => pair.Line).ToArray());
}
