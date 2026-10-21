using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Providers.BusinessCentral.Subscriptions;
using SupportToolkit.Providers.BusinessCentral.Subscriptions.Dtos;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Tests;

public class BusinessCentralEvidenceSourceTests
{
    private static readonly BusinessCentralSourceContext Context =
        new("Sandbox", "Synthetic Company");

    [Fact]
    public async Task Source_MapsProviderDataToSharedEvidence()
    {
        ISmartSubscriptionEvidenceSource source =
            new BusinessCentralSmartSubscriptionEvidenceSource(
                new StubProvider(
                    new BusinessCentralSubscriptionData(
                        [
                            new BusinessCentralSubscriptionHeaderDto
                            {
                                Number = "000000001",
                                SellToCustomerNumber = "BC-1",
                                Blocked = "_x0020_",
                                Status = "Active",
                                StartDate = "2026-01-01",
                                EndDate = "0001-01-01"
                            }
                        ],
                        [
                            new BusinessCentralSubscriptionLineDto
                            {
                                SubscriptionNumber = "000000001",
                                Number = "ITEM-1",
                                Type = "Item",
                                OmitLine = false
                            }
                        ])),
                Context);

        var evidence = await source.GetEvidenceAsync();

        var header = Assert.Single(evidence.Headers);
        Assert.Equal("BC-1", header.CustomerNumber);
        Assert.Null(header.EndDate);

        var line = Assert.Single(evidence.Lines);
        Assert.Equal("Item", line.LineType);
    }

    [Fact]
    public async Task ContextConflict_FailsTheWholeRead()
    {
        var source =
            new BusinessCentralSmartSubscriptionEvidenceSource(
                new StubProvider(
                    new BusinessCentralSubscriptionData(
                        [],
                        [
                            new BusinessCentralSubscriptionLineDto
                            {
                                SubscriptionNumber = "000000001",
                                Company = "Another Company"
                            }
                        ])),
                Context);

        await Assert.ThrowsAsync<BusinessCentralContractException>(
            () => source.GetEvidenceAsync());
    }

    [Fact]
    public void CoreMonitoringScope_DoesNotDependOnProvidersOrModules()
    {
        var coreTypes =
            typeof(ISmartSubscriptionEvidenceSource).Assembly
                .GetTypes()
                .Where(t => t.Namespace ==
                    "SupportToolkit.Core.MonitoringScope");

        Assert.NotEmpty(coreTypes);

        var referenced =
            coreTypes
                .SelectMany(
                    t => t.GetProperties().Select(p => p.PropertyType)
                        .Concat(t.GetConstructors()
                            .SelectMany(c => c.GetParameters())
                            .Select(p => p.ParameterType))
                        .Concat(t.GetMethods()
                            .SelectMany(m => m.GetParameters())
                            .Select(p => p.ParameterType)))
                .Select(t => t.FullName ?? string.Empty);

        Assert.DoesNotContain(
            referenced,
            name => name.Contains("SupportToolkit.Providers.")
                || name.Contains("SupportToolkit.Modules."));
    }

    private sealed class StubProvider(
        BusinessCentralSubscriptionData data)
        : IBusinessCentralSubscriptionProvider
    {
        public Task<BusinessCentralSubscriptionData> GetSubscriptionsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(data);
        }
    }
}
