using SupportToolkit.Core.MonitoringScope;

namespace SupportToolkit.Providers.BusinessCentral.Subscriptions;

/// <summary>
/// Business Central implementation of the shared monitoring-scope
/// evidence contract: reads the raw pages, validates the source
/// context and maps to normalized evidence.
/// </summary>
public sealed class BusinessCentralSmartSubscriptionEvidenceSource
    : ISmartSubscriptionEvidenceSource
{
    private readonly IBusinessCentralSubscriptionProvider _provider;
    private readonly BusinessCentralSourceContext _context;

    public BusinessCentralSmartSubscriptionEvidenceSource(
        IBusinessCentralSubscriptionProvider provider,
        BusinessCentralSourceContext context)
    {
        _provider =
            provider
            ?? throw new ArgumentNullException(nameof(provider));

        _context =
            context
            ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<SmartSubscriptionEvidence> GetEvidenceAsync(
        CancellationToken cancellationToken = default)
    {
        var data =
            await _provider.GetSubscriptionsAsync(
                cancellationToken
            );

        return new SmartSubscriptionEvidence(
            BusinessCentralSubscriptionMapper.MapHeaders(
                data.Headers
            ),
            BusinessCentralSubscriptionMapper.MapLines(
                data.Lines,
                _context
            )
        );
    }
}
