using SupportToolkit.Core.CustomerMapping.Matching;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Modules.UsageReport.Models;
using SupportToolkit.Modules.UsageReport.Production;
using SupportToolkit.Modules.UsageReport.Services;

namespace SupportToolkit.Modules.UsageReport.Fixtures;

/// <summary>
/// Synthetic customers, subscriptions and storage for fixture mode and
/// tests, already in the module's own types. Entirely fictional. It covers
/// every kind of row: equal, different, not active, amount missing, no
/// tenant, no usage reported yet, a total that is not backup + Microsoft
/// 365, and usage without a subscription.
/// </summary>
public sealed class FixtureUsageReportSources : IUsageReportSources
{
    public const string BackupItem = "EXAMPLE-BACKUP";

    public static IReadOnlyList<string> BackupItems { get; } = [BackupItem];

    private const string OtherItem = "EXAMPLE-OTHER";

    private sealed record Customer(
        string Number,
        string Name,
        string Status,
        decimal? Quantity,
        decimal? Amount,
        string? TenantId,
        decimal Backup,
        decimal Microsoft365,
        decimal? Total = null,
        bool UsageReported = true);

    private static readonly Customer[] Customers =
    [
        // Billed exactly what is reported.
        new("C-0001", "Fjeldsted Example Revision", "Active", 1000m, 100m,
            "11111111-0000-4000-8000-000000000001", 400e9m, 600e9m),

        // Billed 20 GB less than is reported.
        new("C-0002", "Nordvik Example Logistik", "Active", 500m, 50m,
            "11111111-0000-4000-8000-000000000002", 320e9m, 200e9m),

        // The subscription is suspended: nothing active to compare.
        new("C-0003", "Saltholm Example Marine", "Suspended", 100m, 10m,
            "11111111-0000-4000-8000-000000000003", 60e9m, 40e9m),

        // The amount is missing, and the total is not backup + M365.
        new("C-0004", "Ravnsborg Example Advokater", "Active", 250m, null,
            "11111111-0000-4000-8000-000000000004", 100e9m, 130e9m, 240e9m),

        // No tenant.
        new("C-0005", "Kildebakke Example Byg", "Active", 300m, 30m,
            null, 0m, 0m),

        // Set up on both sides, but no usage has been reported yet.
        new("C-0006", "Bakkegaard Example Tandlaeger", "Active", 50m, 5m,
            "11111111-0000-4000-8000-000000000006", 0m, 0m, UsageReported: false)
    ];

    private const string UnbilledTenant = "11111111-0000-4000-8000-0000000000a1";
    private const string EmptyTenant = "11111111-0000-4000-8000-0000000000a2";

    public Task<IReadOnlyList<SmartSubscriptionHeader>> GetSubscriptionHeadersAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SmartSubscriptionHeader>>(
            Customers
                .Select(
                    (customer, index) => new SmartSubscriptionHeader(
                        Subscription(index),
                        customer.Number,
                        customer.Status,
                        new DateOnly(2025, 1, 1),
                        null))
                .ToArray());

    public Task<IReadOnlyList<SubscriptionBillingLine>> GetBillingLinesAsync(
        CancellationToken cancellationToken)
    {
        var lines = new List<SubscriptionBillingLine>();

        for (var index = 0; index < Customers.Length; index++)
        {
            var customer = Customers[index];

            lines.Add(
                new SubscriptionBillingLine(
                    Subscription(index), 10000, BackupItem, "Example backup storage",
                    "GB", customer.Quantity, 0.10m, 0m, customer.Amount, false));

            // A line for something else, which the report ignores.
            lines.Add(
                new SubscriptionBillingLine(
                    Subscription(index), 20000, OtherItem, "Example other service",
                    null, 1m, 25m, 0m, 25m, false));
        }

        return Task.FromResult<IReadOnlyList<SubscriptionBillingLine>>(lines);
    }

    public Task<ScopedCustomerParties> GetCustomerPartiesAsync(
        IReadOnlyCollection<string> customerNumbers,
        CancellationToken cancellationToken)
    {
        var wanted =
            customerNumbers.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parties =
            Customers
                .Where(customer => wanted.Contains(customer.Number))
                .Select(customer => new CustomerParty(customer.Number, customer.Name))
                .ToArray();

        return Task.FromResult(new ScopedCustomerParties(parties, 0, 0));
    }

    public Task<UsageTenants> GetCustomerTenantsAsync(
        CancellationToken cancellationToken)
    {
        var tenants =
            Customers
                .Where(customer => customer.TenantId is not null)
                .Select(customer => new CustomerParty(customer.TenantId!, customer.Name))
                .Append(new CustomerParty(UnbilledTenant, "Hvidbjerg Example Consulting"))
                .Append(new CustomerParty(EmptyTenant, "Tranum Example Holding"))
                .ToArray();

        return Task.FromResult(new UsageTenants(tenants, null));
    }

    public Task<UsageStorage> GetStorageAsync(
        IReadOnlyCollection<string> tenantIds,
        CancellationToken cancellationToken)
    {
        var all =
            Customers
                .Where(customer => customer.TenantId is not null && customer.UsageReported)
                .Select(customer => (
                    Id: customer.TenantId!,
                    Storage: Storage(customer.Backup, customer.Microsoft365, customer.Total)))
                .Append((Id: UnbilledTenant, Storage: Storage(50e9m, 25e9m, null)))
                .Append((Id: EmptyTenant, Storage: Storage(0m, 0m, null)));

        var wanted = tenantIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // A tenant without usage has no entry: nothing reported.
        var byTenant =
            all
                .Where(tenant => wanted.Contains(tenant.Id))
                .ToDictionary(
                    tenant => tenant.Id,
                    tenant => tenant.Storage,
                    StringComparer.OrdinalIgnoreCase);

        // The same caveats the real figures carry.
        return Task.FromResult(
            new UsageStorage(
                byTenant,
                "2026-11-01T00:00:00Z",
                AcronisStorageReader.Caveats));
    }

    public void Dispose()
    {
    }

    private static string Subscription(int index) =>
        $"S-{index + 1:0000}";

    private static TenantStorage Storage(
        decimal backup,
        decimal microsoft365,
        decimal? total) =>
        new(
            new UsageMeasurement(total ?? backup + microsoft365, null),
            new UsageMeasurement(backup, null),
            new UsageMeasurement(microsoft365, null));
}
