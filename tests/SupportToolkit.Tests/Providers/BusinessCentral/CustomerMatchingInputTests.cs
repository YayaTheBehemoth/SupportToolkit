using SupportToolkit.Cli.Commands;
using SupportToolkit.Core.CustomerMapping;
using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;

namespace SupportToolkit.Tests;

public class CustomerMatchingInputTests
{
    private const string Item = "ITEM-1";
    private const string TenantA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string TenantB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
    private const string OwnCustomer = "cccccccc-cccc-cccc-cccc-cccccccccccc";
    private const string OwnPartner = "dddddddd-dddd-dddd-dddd-dddddddddddd";

    private static readonly DateOnly AsOf = new(2026, 10, 15);

    [Fact]
    public void BcParties_TakeTheNameFromHeaderRows()
    {
        var result =
            BusinessCentralCustomerPartyMapper.Map(
                ["BC-1", "BC-2", "BC-3"],
                [
                    Row("SUB-1", "BC-1", "Northwind   Traders"),
                    Row("SUB-2", "BC-1", "northwind traders"),
                    Row("SUB-3", "BC-2", "Contoso"),
                    Row("SUB-4", "BC-2", "Contoso Holding"),
                    null
                ]);

        var byId = result.Parties.ToDictionary(party => party.Id);

        Assert.Equal("Northwind Traders", byId["BC-1"].Name);
        Assert.Null(byId["BC-2"].Name);
        Assert.Null(byId["BC-3"].Name);

        Assert.Equal(1, result.CustomersWithConflictingNames);
        Assert.Equal(2, result.CustomersWithoutName);
        Assert.Equal(1, result.CustomersWithName);
    }

    [Fact]
    public void BcParties_IgnoreRowsWithoutACustomerNumberOrName()
    {
        var result =
            BusinessCentralCustomerPartyMapper.Map(
                ["BC-1"],
                [
                    Row("SUB-1", null, "Northwind"),
                    Row("SUB-2", "BC-1", "   "),
                    Row("SUB-3", "BC-9", "Contoso")
                ]);

        Assert.Null(Assert.Single(result.Parties).Name);
        Assert.Equal(1, result.CustomersWithoutName);
        Assert.Equal(0, result.CustomersWithConflictingNames);
    }

    [Fact]
    public void AcronisParties_ExcludeTheOperatorPartnersUnitsAndDeleted()
    {
        var build =
            AcronisCustomerParties.ForResolution(
            [
                Tenant(OwnPartner, "SYNTHETIC OPERATOR", "partner"),
                Tenant(OwnCustomer, "Synthetic Operator", "customer"),
                Tenant(TenantA, "Northwind Traders", "customer"),
                Tenant(TenantB, "Contoso", "customer"),
                Tenant(Guid.NewGuid().ToString(), "Unit", "unit"),
                new TenantDto
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Deleted",
                    Kind = "customer",
                    DeletedAt = DateTimeOffset.UtcNow
                }
            ]);

        Assert.True(build.OperatorTenantExcluded);
        Assert.Null(build.Notice);
        Assert.Equal(1, build.PartnerTenants);
        Assert.Equal(1, build.DeletedCustomerTenants);
        Assert.Equal(
            [TenantA, TenantB],
            build.Parties.Select(party => party.Id).Order());
        Assert.DoesNotContain(build.Parties, party => party.Id == OwnCustomer);
    }

    [Fact]
    public async Task Report_PrintsCountsOnlyUnlessNamesAreRequested()
    {
        var bc =
            new ScopedCustomerParties(
                [new("BC-9", "Secret Corp")],
                0,
                0);

        var acronis =
            new AcronisPartyBuild(
                [new(TenantA, "Secret Corp")],
                true,
                null);

        var result =
            await new AutoCustomerMappingResolver()
                .ResolveAsync(acronis.Parties, bc.Parties);

        var hidden = new StringWriter();

        CustomerMatchingDevCommand.WriteReport(
            hidden, [], bc, acronis, result, false);

        var text = hidden.ToString();

        Assert.DoesNotContain("Secret Corp", text);
        Assert.DoesNotContain("BC-9", text);
        Assert.Contains("accepted automatically:   1", text);

        var shown = new StringWriter();

        CustomerMatchingDevCommand.WriteReport(
            shown, [], bc, acronis, result, true);

        Assert.Contains("\"Secret Corp\"", shown.ToString());
        Assert.Contains("BC BC-9", shown.ToString());
    }

    [Fact]
    public async Task Report_IgnoresSuspendedAndBlockedCustomersWithoutATenant()
    {
        var observations =
            new SmartSubscriptionEntitlementEvaluator(Item)
                .Evaluate(
                    [
                        Header("SUB-1", "BC-ACTIVE", "Active"),
                        Header("SUB-2", "BC-SUSPENDED", "Suspended"),
                        Header("SUB-3", "BC-BLOCKED", "Active", "All"),
                        Header("SUB-4", "BC-UNCLEAR", "Open")
                    ],
                    [Line("SUB-1"), Line("SUB-2"), Line("SUB-3"), Line("SUB-4")],
                    AsOf);

        var bc =
            new ScopedCustomerParties(
                [
                    new("BC-ACTIVE", "Active Corp"),
                    new("BC-SUSPENDED", "Suspended Corp"),
                    new("BC-BLOCKED", "Blocked Corp"),
                    new("BC-UNCLEAR", "Unclear Corp")
                ],
                0,
                0);

        var acronis =
            new AcronisPartyBuild([], false, null);

        var result =
            await new AutoCustomerMappingResolver()
                .ResolveAsync(acronis.Parties, bc.Parties);

        var counts = new StringWriter();

        CustomerMatchingDevCommand.WriteReport(
            counts, observations, bc, acronis, result, false);

        Assert.Contains(
            "BC customers with no candidate tenant: 2 (active 1, unclear 1)",
            counts.ToString());

        var worksheet = new StringWriter();

        CustomerMatchingDevCommand.WriteReport(
            worksheet, observations, bc, acronis, result, true);

        var text = worksheet.ToString();

        Assert.Contains("Active Corp", text);
        Assert.Contains("Unclear Corp", text);
        Assert.DoesNotContain("Suspended Corp", text);
        Assert.DoesNotContain("Blocked Corp", text);
    }

    [Fact]
    public async Task Report_StatesWhenTheNamesCouldNotBeRead()
    {
        var bc =
            new ScopedCustomerParties(
                [new("BC-1", null)],
                1,
                0,
                "Bad request.\nnewline");

        var acronis =
            new AcronisPartyBuild([], false, null);

        var result =
            await new AutoCustomerMappingResolver()
                .ResolveAsync(acronis.Parties, bc.Parties);

        var writer = new StringWriter();

        CustomerMatchingDevCommand.WriteReport(
            writer, [], bc, acronis, result, false);

        Assert.Contains("NAME READ FAILED", writer.ToString());
        Assert.DoesNotContain("\nnewline", writer.ToString());
    }

    private static SmartSubscriptionHeader Header(
        string number,
        string customer,
        string status,
        string blocked = " ")
    {
        return new SmartSubscriptionHeader(
            number,
            customer,
            status,
            new DateOnly(2026, 1, 1),
            null,
            null,
            blocked);
    }

    private static SmartSubscriptionLine Line(
        string subscription)
    {
        return new SmartSubscriptionLine(
            subscription,
            Item,
            "Item",
            false);
    }

    private static BusinessCentralHeaderCustomerNameDto Row(
        string subscription,
        string? customerNumber,
        string name)
    {
        return new BusinessCentralHeaderCustomerNameDto
        {
            Number = subscription,
            SellToCustomerNumber = customerNumber,
            SellToCustomerName = name
        };
    }

    private static TenantDto Tenant(
        string id,
        string name,
        string kind)
    {
        return new TenantDto
        {
            Id = id,
            Name = name,
            Kind = kind
        };
    }
}
