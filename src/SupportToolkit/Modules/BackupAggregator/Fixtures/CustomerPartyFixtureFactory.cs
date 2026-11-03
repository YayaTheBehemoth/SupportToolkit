using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;

namespace SupportToolkit.Modules.BackupAggregator.Fixtures;

/// <summary>
/// Fully fictional matching evidence.
///
/// Acronis parties are derived from the synthetic tenants in
/// MonitoringTenantSelectionFixtureFactory through the same adapter as
/// production, so they carry id and name only.
///
/// Business Central parties use the synthetic customers in
/// SmartSubscriptionFixtureFactory. No BC customer source exists yet;
/// the invented CVR numbers and ".test" domains only show that keys
/// absent on the Acronis side cannot match. The scenarios are:
/// Northwind, Alpine and Fabrikam match on name after normalization,
/// Contoso's names differ, and Adventure Works has a name shared by
/// two customers.
/// </summary>
public static class CustomerPartyFixtureFactory
{
    public static (
        IReadOnlyList<CustomerParty> Acronis,
        IReadOnlyList<CustomerParty> BusinessCentral
    ) Create()
    {
        var acronis =
            AcronisCustomerParties.FromCustomerTenants(
                MonitoringTenantSelectionFixtureFactory
                    .Create()
                    .Tenants
            );

        IReadOnlyList<CustomerParty> businessCentral =
        [
            new(
                "BC-NORTHWIND",
                "Northwind Manufacturing A/S",
                "10000001",
                "invoice@northwind.test"
            ),

            new(
                "BC-CONTOSO",
                "Contoso Værksted ApS",
                null,
                "finance@contoso.test"
            ),

            new(
                "BC-ALPINE",
                "Alpine  Consulting ApS",
                null,
                null
            ),

            new(
                "BC-FABRIKAM",
                "Fabrikam Logistics",
                "DK10000004",
                null
            ),

            new(
                "BC-FABRIKAM-HOLDING",
                "Fabrikam Holding",
                null,
                "board@fabrikam.test"
            ),

            new(
                "BC-ADVENTURE",
                "Adventure Works ApS",
                null,
                null
            ),

            new(
                "BC-ADVENTURE-OLD",
                "Adventure Works I/S",
                null,
                null
            )
        ];

        return (
            acronis,
            businessCentral
        );
    }
}
