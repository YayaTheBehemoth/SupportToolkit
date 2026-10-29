using SupportToolkit.Cli.Commands;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Usage;

namespace SupportToolkit.Tests.Cli;

public class AcronisUsageDevCommandTests
{
    private const string Partner = "00000000-0000-0000-0000-0000000000a1";
    private const string Alpha = "11111111-1111-1111-1111-111111111111";
    private const string Beta = "22222222-2222-2222-2222-222222222222";
    private const string Gamma = "33333333-3333-3333-3333-333333333333";

    [Theory]
    [InlineData("--production")]
    [InlineData("--production", "--names")]
    [InlineData("--production", "--names", "--out", "usage.csv")]
    public void Arguments_AcceptTheDocumentedShapes(params string[] args)
    {
        Assert.True(
            AcronisUsageDevCommand.TryParseArguments(
                args,
                out var options,
                out _));

        Assert.NotNull(options);
    }

    [Theory]
    [InlineData]
    [InlineData("--names")]
    [InlineData("--production", "--out", "usage.csv")]
    [InlineData("--production", "--names", "--out")]
    [InlineData("--production", "--names", "--names")]
    [InlineData("--production", "--unknown")]
    public void Arguments_RejectEverythingElse(params string[] args)
    {
        Assert.False(
            AcronisUsageDevCommand.TryParseArguments(
                args,
                out var options,
                out var error));

        Assert.Null(options);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void DefaultOutput_IsAggregateOnly_AndNeverRevealsNames()
    {
        var output = new StringWriter();

        var exit =
            AcronisUsageDevCommand.WriteReport(
                output,
                Tenants(),
                Usage(),
                showNames: false);

        var text = output.ToString();

        Assert.DoesNotContain("Alpha Example", text);
        Assert.DoesNotContain("Beta Example", text);
        Assert.Contains("Customer tenants:              3", text);
        Assert.Contains("With usage returned:         2", text);
        Assert.Contains("No usage returned (unknown): 1", text);
        Assert.Contains("storage [bytes]", text);
        Assert.Contains("Customer names hidden", text);

        // Missing data makes the run incomplete, not clean.
        Assert.Equal(2, exit);
    }

    [Fact]
    public void Units_AreShownAsReported_NeverConverted()
    {
        var output = new StringWriter();

        AcronisUsageDevCommand.WriteReport(
            output,
            Tenants(),
            Usage(),
            showNames: false);

        var text = output.ToString();

        Assert.Contains("[bytes]", text);
        Assert.Contains("min 1000, median 1500, p90 2000, max 2000", text);
    }

    [Fact]
    public void MissingValueOrUnit_IsUnknownNeverZero()
    {
        var rows =
            AcronisUsageDevCommand.BuildRows(
                Tenants(),
                Usage());

        var gamma = Assert.Single(rows, row => row.TenantId == Gamma);
        Assert.Equal("unknown: no usage returned", gamma.Status);
        Assert.Null(gamma.Value);

        var noUnit = Assert.Single(rows, row => row.Metric == "local_storage");
        Assert.Equal("unknown: value missing; unit missing", noUnit.Status);
        Assert.Null(noUnit.Value);

        Assert.DoesNotContain(rows, row => row.Value == 0m);
    }

    [Fact]
    public void NamesMode_WritesOneRowPerMeasurement_WithParentAndUnknownRows()
    {
        var output = new StringWriter();

        AcronisUsageDevCommand.WriteReport(
            output,
            Tenants(),
            Usage(),
            showNames: true);

        var text = output.ToString();

        Assert.Contains(
            "tenant_id,tenant_name,parent_tenant_id,parent_tenant_name," +
            "metric,edition,value,absolute_value,unit,range_start,status",
            text);

        Assert.Contains(
            $"{Alpha},Alpha Example,{Partner},Example Partner," +
            "storage,standard,1000,1000,bytes,2026-10-01T00:00:00,ok",
            text);

        Assert.Contains(
            $"{Gamma},Gamma Example,{Partner},Example Partner,,,unknown," +
            "unknown,,,unknown: no usage returned",
            text);

        // The partner tenant is not a customer and has no row.
        Assert.DoesNotContain(
            text.Split(Environment.NewLine),
            line => line.StartsWith(Partner, StringComparison.Ordinal));
    }

    [Fact]
    public void Csv_NeutralizesFormulasAndQuotesSpecialCharacters()
    {
        TenantDto[] tenants =
        [
            new() { Id = Alpha, Name = "=cmd|' /c calc'!A1", Kind = "customer" },
            new() { Id = Beta, Name = "Example, \"Quoted\" Ltd", Kind = "customer" }
        ];

        var output = new StringWriter();

        AcronisUsageDevCommand.WriteReport(
            output,
            tenants,
            new AcronisUsageResult(
                new Dictionary<string, IReadOnlyList<AcronisTenantUsage>>(),
                0),
            showNames: true);

        var text = output.ToString();

        Assert.Contains($"{Alpha},'=cmd|' /c calc'!A1,", text);
        Assert.Contains("\"Example, \"\"Quoted\"\" Ltd\"", text);
    }

    [Fact]
    public void OutputPath_InsideAGitWorkingTree_IsRefused()
    {
        using var repo = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(repo.Path, ".git"));
        Directory.CreateDirectory(Path.Combine(repo.Path, "nested"));

        Assert.False(
            AcronisUsageDevCommand.TryValidateOutputPath(
                Path.Combine(repo.Path, "nested", "usage.csv"),
                out _,
                out var error));

        Assert.Contains("git working tree", error);
    }

    [Fact]
    public void OutputPath_OutsideAnyRepository_IsAccepted_AndWrittenWithNames()
    {
        using var directory = new TempDirectory();

        var path = Path.Combine(directory.Path, "usage.csv");

        Assert.True(
            AcronisUsageDevCommand.TryValidateOutputPath(
                path,
                out var fullPath,
                out _));

        var console = new StringWriter();

        AcronisUsageDevCommand.WriteReport(
            console,
            Tenants(),
            Usage(),
            showNames: true,
            fullPath);

        var file = File.ReadAllText(path);

        Assert.Contains("Alpha Example", file);
        Assert.DoesNotContain("Alpha Example", console.ToString());
        Assert.Contains("customer names", console.ToString());
    }

    [Fact]
    public void OutputPath_ExistingFileDirectoryOrMissingFolder_IsRefused()
    {
        using var directory = new TempDirectory();

        var existing = Path.Combine(directory.Path, "existing.csv");
        File.WriteAllText(existing, "keep");

        Assert.False(
            AcronisUsageDevCommand.TryValidateOutputPath(
                existing,
                out _,
                out _));

        Assert.Equal("keep", File.ReadAllText(existing));

        Assert.False(
            AcronisUsageDevCommand.TryValidateOutputPath(
                directory.Path,
                out _,
                out _));

        Assert.False(
            AcronisUsageDevCommand.TryValidateOutputPath(
                Path.Combine(directory.Path, "missing", "usage.csv"),
                out _,
                out _));
    }

    private static TenantDto[] Tenants() =>
    [
        new() { Id = Partner, Name = "Example Partner", Kind = "partner" },
        new() { Id = Alpha, Name = "Alpha Example", Kind = "customer", ParentId = Partner },
        new() { Id = Beta, Name = "Beta Example", Kind = "customer", ParentId = Partner },
        new() { Id = Gamma, Name = "Gamma Example", Kind = "customer", ParentId = Partner }
    ];

    private static AcronisUsageResult Usage() =>
        new(
            new Dictionary<string, IReadOnlyList<AcronisTenantUsage>>
            {
                [Alpha] =
                [
                    new(
                        Alpha,
                        "storage",
                        "standard",
                        "infra",
                        1000m,
                        1000m,
                        "bytes",
                        "2026-10-01T00:00:00")
                ],
                [Beta] =
                [
                    new(
                        Beta,
                        "storage",
                        "standard",
                        "infra",
                        2000m,
                        2000m,
                        "bytes",
                        "2026-10-01T00:00:00"),
                    new(
                        Beta,
                        "local_storage",
                        "standard",
                        null,
                        null,
                        null,
                        null,
                        null)
                ]
            },
            0);

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } =
            Directory.CreateDirectory(
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "supporttoolkit-test-" + Guid.NewGuid().ToString("N")))
                .FullName;

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
