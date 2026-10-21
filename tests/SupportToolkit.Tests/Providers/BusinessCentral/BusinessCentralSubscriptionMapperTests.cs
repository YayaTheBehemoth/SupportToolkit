using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Tests;

public class BusinessCentralSubscriptionMapperTests
{
    private static readonly BusinessCentralSourceContext Context =
        new("Sandbox", "Synthetic Company");

    [Fact]
    public void Header_PreservesBothCustomersAndBlockedEvidence()
    {
        var header =
            Assert.Single(
                BusinessCentralSubscriptionMapper.MapHeaders(
                [
                    new BusinessCentralSubscriptionHeaderDto
                    {
                        Number = " SUB-1 ",
                        SellToCustomerNumber = " BC-1 ",
                        BillToCustomerNumber = "BC-2",
                        Status = "Active",
                        Blocked = "All",
                        StartDate = "2026-01-01",
                        EndDate = "2026-12-31"
                    }
                ]));

        Assert.Equal("SUB-1", header.SubscriptionNumber);
        Assert.Equal("BC-1", header.CustomerNumber);
        Assert.Equal("BC-2", header.BillToCustomerNumber);
        Assert.Equal("All", header.BlockedValue);
        Assert.Equal(new DateOnly(2026, 1, 1), header.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 31), header.EndDate);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0001-01-01")]
    [InlineData("0000-12-31")]
    public void EmptyDateRepresentations_MapToNoDate(
        string? raw)
    {
        var header =
            Assert.Single(
                BusinessCentralSubscriptionMapper.MapHeaders(
                [
                    new BusinessCentralSubscriptionHeaderDto
                    {
                        Number = "SUB-1",
                        EndDate = raw
                    }
                ]));

        Assert.Null(header.EndDate);
    }

    [Theory]
    [InlineData("01/02/2026")]
    [InlineData("2026-13-40")]
    [InlineData("tomorrow")]
    public void UnparseableDate_IsCollectionError(
        string raw)
    {
        Assert.Throws<BusinessCentralContractException>(
            () => BusinessCentralSubscriptionMapper.MapHeaders(
            [
                new BusinessCentralSubscriptionHeaderDto
                {
                    Number = "SUB-1",
                    StartDate = raw
                }
            ]));
    }

    [Fact]
    public void MissingBlockedAndBillTo_StayUnreported()
    {
        var header =
            Assert.Single(
                BusinessCentralSubscriptionMapper.MapHeaders(
                [
                    new BusinessCentralSubscriptionHeaderDto
                    {
                        Number = "SUB-1"
                    }
                ]));

        Assert.Null(header.BlockedValue);
        Assert.Null(header.BillToCustomerNumber);
        Assert.Equal(string.Empty, header.Status);
    }

    [Fact]
    public void Line_PreservesTypeAndOmitEvidence()
    {
        var line =
            Assert.Single(
                BusinessCentralSubscriptionMapper.MapLines(
                [
                    Line(type: "Item", omit: true)
                ],
                Context));

        Assert.Equal("SUB-1", line.SubscriptionNumber);
        Assert.Equal("ITEM-1", line.ItemNumber);
        Assert.Equal("Item", line.LineType);
        Assert.True(line.OmitLine);
    }

    [Theory]
    [InlineData("Other Company", "Sandbox")]
    [InlineData("Synthetic Company", "Production")]
    [InlineData("Other Company", null)]
    [InlineData(null, "Production")]
    public void PopulatedContextMismatch_IsCollectionError_NotAFilter(
        string? company,
        string? environment)
    {
        // A valid row precedes the offending one: the whole
        // collection must fail instead of returning the valid row.
        var exception =
            Assert.Throws<BusinessCentralContractException>(
                () => BusinessCentralSubscriptionMapper.MapLines(
                [
                    Line(),
                    Line(company: company, environment: environment)
                ],
                Context));

        Assert.DoesNotContain("Synthetic", exception.Message);
        Assert.DoesNotContain("Sandbox", exception.Message);
    }

    [Fact]
    public void ContextMatching_IgnoresCaseAndWhitespace()
    {
        var lines =
            BusinessCentralSubscriptionMapper.MapLines(
            [
                Line(
                    company: " synthetic company ",
                    environment: "SANDBOX")
            ],
            Context);

        Assert.Single(lines);
    }

    [Fact]
    public void NullRow_IsCollectionError()
    {
        Assert.Throws<BusinessCentralContractException>(
            () => BusinessCentralSubscriptionMapper.MapHeaders(
                [null]));

        Assert.Throws<BusinessCentralContractException>(
            () => BusinessCentralSubscriptionMapper.MapLines(
                [null],
                Context));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "  ")]
    public void BlankRowContext_IsAcceptedAsNotReported(
        string? company,
        string? environment)
    {
        var lines =
            BusinessCentralSubscriptionMapper.MapLines(
                [Line(company: company, environment: environment)],
                Context);

        Assert.Single(lines);
    }

    [Fact]
    public void PopulatedCompany_WithoutConfiguredName_IsCollectionError()
    {
        var noName =
            new BusinessCentralSourceContext("Sandbox", null);

        Assert.Throws<BusinessCentralContractException>(
            () => BusinessCentralSubscriptionMapper.MapLines(
                [Line()],
                noName));

        // Blank rows still pass: there is nothing to contradict.
        Assert.Single(
            BusinessCentralSubscriptionMapper.MapLines(
                [Line(company: "", environment: "")],
                noName));
    }

    [Fact]
    public void ContextFromOptions_UsesConfiguredEnvironmentAndName()
    {
        var withName =
            BusinessCentralOptions.Create(
                "11111111-1111-1111-1111-111111111111",
                "22222222-2222-2222-2222-222222222222",
                "Sandbox",
                "33333333-3333-3333-3333-333333333333",
                "example",
                "monitoring",
                "v1.0",
                "Synthetic Company");

        Assert.Equal(
            Context,
            BusinessCentralSourceContext.From(withName));
    }

    [Fact]
    public void MappedEvidence_FeedsEvaluatorConservatively()
    {
        var headers =
            BusinessCentralSubscriptionMapper.MapHeaders(
            [
                new BusinessCentralSubscriptionHeaderDto
                {
                    Number = "SUB-1",
                    SellToCustomerNumber = "BC-1",
                    BillToCustomerNumber = "BC-1",
                    Status = "Active",
                    Blocked = "true",
                    StartDate = "2026-01-01"
                }
            ]);

        var lines =
            BusinessCentralSubscriptionMapper.MapLines(
                [Line(number: SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber)],
                Context);

        var result =
            Assert.Single(
                new SmartSubscriptionEntitlementEvaluator(
                        SmartSubscriptionFixtureFactory
                            .FixtureMonitoringItemNumber)
                    .Evaluate(
                        headers,
                        lines,
                        new DateOnly(2026, 10, 15)));

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            result.Assessment);
    }

    [Theory]
    [InlineData("_x0020_", " ")]
    [InlineData("All", "All")]
    [InlineData("Invoice", "Invoice")]
    public void BlockedEnumNames_AreDecoded(
        string wire,
        string expected)
    {
        var header =
            Assert.Single(
                BusinessCentralSubscriptionMapper.MapHeaders(
                [
                    new BusinessCentralSubscriptionHeaderDto
                    {
                        Number = "000000001",
                        Blocked = wire
                    }
                ]));

        Assert.Equal(expected, header.BlockedValue);
    }

    [Theory]
    [InlineData("_x0020_", " ")]
    [InlineData("Item", "Item")]
    [InlineData("G_x002F_L_x0020_Account", "G/L Account")]
    [InlineData("Charge_x0020__x0028_Item_x0029_", "Charge (Item)")]
    public void LineTypeEnumNames_AreDecoded(
        string wire,
        string expected)
    {
        var line =
            Assert.Single(
                BusinessCentralSubscriptionMapper.MapLines(
                    [Line(type: wire)],
                    Context));

        Assert.Equal(expected, line.LineType);
    }

    [Fact]
    public void ObservedLiveShapes_ProduceExpectedAssessments()
    {
        // Shapes observed in read-only metadata and sample responses:
        // blank blocked is "_x0020_", empty dates are "0001-01-01".
        var headers =
            BusinessCentralSubscriptionMapper.MapHeaders(
            [
                new BusinessCentralSubscriptionHeaderDto
                {
                    Number = "000000001",
                    SellToCustomerNumber = "BC-1",
                    BillToCustomerNumber = "BC-1",
                    Blocked = "_x0020_",
                    Status = "Active",
                    StartDate = "2026-01-01",
                    EndDate = "0001-01-01"
                },
                new BusinessCentralSubscriptionHeaderDto
                {
                    Number = "000000025",
                    SellToCustomerNumber = "BC-2",
                    BillToCustomerNumber = "BC-2",
                    Blocked = "All",
                    Status = "Active",
                    StartDate = "2026-01-01",
                    EndDate = "0001-01-01"
                }
            ]);

        var monitoring =
            SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber;

        var lines =
            BusinessCentralSubscriptionMapper.MapLines(
            [
                Line(number: monitoring, subscription: "000000001"),
                Line(number: monitoring, subscription: "000000025")
            ],
            Context);

        var results =
            new SmartSubscriptionEntitlementEvaluator(monitoring)
                .Evaluate(headers, lines, new DateOnly(2026, 10, 15));

        Assert.Equal(
            MonitoringEntitlementAssessment.ActiveCandidate,
            results.Single(r => r.SubscriptionNumber == "000000001")
                .Assessment);

        Assert.Equal(
            MonitoringEntitlementAssessment.NeedsReview,
            results.Single(r => r.SubscriptionNumber == "000000025")
                .Assessment);
    }

    private static BusinessCentralSubscriptionLineDto Line(
        string? type = "Item",
        bool? omit = false,
        string number = "ITEM-1",
        string subscription = "SUB-1",
        string? company = "Synthetic Company",
        string? environment = "Sandbox")
    {
        return new BusinessCentralSubscriptionLineDto
        {
            SubscriptionNumber = subscription,
            Number = number,
            Type = type,
            OmitLine = omit,
            Company = company,
            Environment = environment
        };
    }
}
