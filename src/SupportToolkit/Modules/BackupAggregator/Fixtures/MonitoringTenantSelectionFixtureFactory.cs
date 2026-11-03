
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Modules.BackupAggregator.Fixtures;

/// <summary>
/// Fully fictional Acronis tenant records and resolved identities.
///
/// These relationships exist solely to test tenant-selection
/// behavior. They are not a proposed manual production registry.
/// </summary>
public static class MonitoringTenantSelectionFixtureFactory
{
    public static (
        IReadOnlyList<TenantDto> Tenants,
        IReadOnlyList<ResolvedMonitoringCustomerIdentity> Identities
    ) Create()
    {
        IReadOnlyList<TenantDto> tenants =
        [
            Tenant(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Northwind Manufacturing"
            ),

            Tenant(
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                "Contoso Workshop"
            ),

            Tenant(
                "cccccccc-cccc-cccc-cccc-cccccccccccc",
                "Alpine Consulting"
            ),

            Tenant(
                "dddddddd-dddd-dddd-dddd-dddddddddddd",
                "Fabrikam Logistics"
            ),

            Tenant(
                "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
                "Adventure Works"
            )
        ];

        IReadOnlyList<ResolvedMonitoringCustomerIdentity> identities =
        [
            new(
                "BC-NORTHWIND",
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            ),

            new(
                "BC-CONTOSO",
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
            ),

            new(
                "BC-ALPINE",
                "cccccccc-cccc-cccc-cccc-cccccccccccc"
            ),

            new(
                "BC-FABRIKAM",
                "dddddddd-dddd-dddd-dddd-dddddddddddd"
            ),

            new(
                "BC-ADVENTURE",
                "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
            )
        ];

        return (
            tenants,
            identities
        );
    }

    private static TenantDto Tenant(
        string id,
        string name)
    {
        return new TenantDto
        {
            Id = id,
            Name = name,
            Kind = "customer",
            Enabled = true
        };
    }
}
