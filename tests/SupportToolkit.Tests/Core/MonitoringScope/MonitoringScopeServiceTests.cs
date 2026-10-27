using System.Net;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Tests;

public class MonitoringScopeServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 10, 15);

    [Fact]
    public async Task EachModule_ScopesWithItsOwnItemNumbers()
    {
        var service =
            new MonitoringScopeService(
                new Evidence(
                    [
                        Header("SUB-1", "BC-1", "Active"),
                        Header("SUB-2", "BC-2", "Active")
                    ],
                    [
                        new SmartSubscriptionLine("SUB-1", "ITEM-A", "Item", false),
                        new SmartSubscriptionLine("SUB-2", "ITEM-B", "Item", false)
                    ]));

        var first =
            await service.ResolveAsync(["ITEM-A"], AsOf);

        var second =
            await service.ResolveAsync(["ITEM-A", "ITEM-B"], AsOf);

        Assert.Equal(["BC-1"], first.CustomerNumbers);
        Assert.Equal(["BC-1", "BC-2"], second.CustomerNumbers);
        Assert.Equal(CustomerEntitlementState.Active, second.StateOf("BC-2"));
    }

    [Fact]
    public async Task ServiceItems_WidenTheCustomers_ButNeverEntitleAnyone()
    {
        var service =
            new MonitoringScopeService(
                new Evidence(
                    [
                        Header("SUB-1", "BC-1", "Active"),
                        Header("SUB-2", "BC-2", "Active"),
                        Header("SUB-3", "BC-3", "Active")
                    ],
                    [
                        new SmartSubscriptionLine("SUB-1", "ADD-ON", "Item", false),
                        new SmartSubscriptionLine("SUB-1", "EXAMPLE-BACKUP", "Item", false),
                        new SmartSubscriptionLine("SUB-2", "EXAMPLE-BACKUP", "Item", false),
                        new SmartSubscriptionLine("SUB-3", "EXAMPLE BACKUP PLUS", "Item", false)
                    ]));

        var result =
            await service.ResolveAsync(
                ["ADD-ON"],
                AsOf,
                serviceItemNumbers: ["EXAMPLE-BACKUP", "EXAMPLE BACKUP PLUS"]);

        // Only the add-on entitles.
        Assert.Equal(["BC-1"], result.CustomerNumbers);

        // The service items add the other customers of the service.
        Assert.Equal(
            ["BC-1", "BC-2", "BC-3"],
            result.ServiceCustomerNumbers);

        Assert.Empty(result.EntitledWithoutServiceItem);
    }

    [Fact]
    public async Task EntitledCustomerWithoutAServiceItem_IsReported()
    {
        var result =
            await new MonitoringScopeService(
                new Evidence(
                    [Header("SUB-1", "BC-1", "Active")],
                    [new SmartSubscriptionLine("SUB-1", "ADD-ON", "Item", false)]))
                .ResolveAsync(
                    ["ADD-ON"],
                    AsOf,
                    serviceItemNumbers: ["EXAMPLE-BACKUP"]);

        Assert.Equal(["BC-1"], result.EntitledWithoutServiceItem);
        Assert.Equal(["BC-1"], result.ServiceCustomerNumbers);
    }

    [Fact]
    public async Task WithoutServiceItems_ServiceCustomersAreTheEntitledOnes()
    {
        var result =
            await new MonitoringScopeService(
                new Evidence(
                    [Header("SUB-1", "BC-1", "Active")],
                    [new SmartSubscriptionLine("SUB-1", "ADD-ON", "Item", false)]))
                .ResolveAsync(["ADD-ON"], AsOf);

        Assert.Equal(["BC-1"], result.ServiceCustomerNumbers);
        Assert.Empty(result.EntitledWithoutServiceItem);
    }

    [Fact]
    public async Task UnknownCustomer_IsNeverAssumedActive()
    {
        var result =
            await new MonitoringScopeService(new Evidence([], []))
                .ResolveAsync(["ITEM-A"], AsOf);

        Assert.Equal(
            CustomerEntitlementState.NeedsReview,
            result.StateOf("BC-404"));
    }

    [Fact]
    public async Task EvidenceIsCarried_ForReconciliation()
    {
        var headers = new[] { Header("SUB-1", "BC-1", "Active") };

        var result =
            await new MonitoringScopeService(new Evidence(headers, []))
                .ResolveAsync(["ITEM-A"], AsOf);

        Assert.Same(headers, result.Evidence.Headers);
    }

    [Fact]
    public void Selector_DoesNotCountSuspendedCustomersWithoutATenantAsUnmatched()
    {
        var observations =
            new SmartSubscriptionEntitlementEvaluator("ITEM-A")
                .Evaluate(
                    [
                        Header("SUB-1", "BC-ACTIVE", "Active"),
                        Header("SUB-2", "BC-SUSPENDED", "Suspended")
                    ],
                    [
                        new SmartSubscriptionLine("SUB-1", "ITEM-A", "Item", false),
                        new SmartSubscriptionLine("SUB-2", "ITEM-A", "Item", false)
                    ],
                    AsOf);

        var result =
            new MonitoringTenantSelector()
                .Select(
                    [new TenantDto
                    {
                        Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                        Name = "Other",
                        Kind = "customer"
                    }],
                    observations,
                    []);

        Assert.Equal(
            ["BC-ACTIVE"],
            result.UnmatchedEvidence.Select(o => o.CustomerNumber));
    }

    [Fact]
    public async Task NameSource_MapsNamesForTheRequestedCustomers()
    {
        var source =
            new BusinessCentralCustomerNameSource(
                new Names(
                [
                    new BusinessCentralHeaderCustomerNameDto
                    {
                        Number = "SUB-1",
                        SellToCustomerNumber = "BC-1",
                        SellToCustomerName = "Northwind Traders"
                    }
                ]));

        var result =
            await source.GetCustomerPartiesAsync(["BC-1", "BC-2"]);

        Assert.Null(result.Failure);
        Assert.Equal("Northwind Traders", result.Parties[0].Name);
        Assert.Equal(1, result.CustomersWithoutName);
    }

    [Fact]
    public async Task NameSource_ReportsAnApiFailure_WithoutLosingTheCustomers()
    {
        var source =
            new BusinessCentralCustomerNameSource(
                new Names(
                    new BusinessCentralApiException(
                        HttpStatusCode.BadRequest,
                        "Business Central returned an unexpected response (HTTP 400).")));

        var result =
            await source.GetCustomerPartiesAsync(["BC-1", "BC-2"]);

        Assert.Contains("HTTP 400", result.Failure);
        Assert.Equal(2, result.Parties.Count);
        Assert.All(result.Parties, party => Assert.Null(party.Name));
        Assert.Equal(2, result.CustomersWithoutName);
    }

    [Fact]
    public async Task NameSource_DoesNotSwallowUnexpectedErrors()
    {
        var source =
            new BusinessCentralCustomerNameSource(
                new Names(new InvalidOperationException("boom")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.GetCustomerPartiesAsync(["BC-1"]));
    }

    private static SmartSubscriptionHeader Header(
        string number,
        string customer,
        string status)
    {
        return new SmartSubscriptionHeader(
            number,
            customer,
            status,
            new DateOnly(2026, 1, 1),
            null,
            null,
            " ");
    }

    private sealed class Evidence(
        IReadOnlyList<SmartSubscriptionHeader> headers,
        IReadOnlyList<SmartSubscriptionLine> lines)
        : ISmartSubscriptionEvidenceSource
    {
        public Task<SmartSubscriptionEvidence> GetEvidenceAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new SmartSubscriptionEvidence(headers, lines));
        }
    }

    private sealed class Names : IBusinessCentralCustomerNameProvider
    {
        private readonly IReadOnlyList<BusinessCentralHeaderCustomerNameDto?>? _rows;
        private readonly Exception? _failure;

        public Names(IReadOnlyList<BusinessCentralHeaderCustomerNameDto?> rows)
        {
            _rows = rows;
        }

        public Names(Exception failure)
        {
            _failure = failure;
        }

        public Task<IReadOnlyList<BusinessCentralHeaderCustomerNameDto?>>
            GetHeaderCustomerNamesAsync(
                CancellationToken cancellationToken = default)
        {
            return _failure is not null
                ? throw _failure
                : Task.FromResult(_rows!);
        }
    }
}
