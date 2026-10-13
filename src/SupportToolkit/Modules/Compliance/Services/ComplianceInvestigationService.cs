
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;

namespace SupportToolkit.Modules.Compliance.Services;

/// <summary>
/// Locates a device in a complete retrieved inventory.
///
/// This is evidence retrieval, not remediation,
/// follow-up management or a compliance decision engine.
/// </summary>
public sealed class ComplianceInvestigationService
{
    private readonly IGraphManagedDeviceProvider _provider;

    public ComplianceInvestigationService(
        IGraphManagedDeviceProvider provider)
    {
        _provider = provider
            ?? throw new ArgumentNullException(nameof(provider));
    }

    public async Task<ManagedDevice?> InvestigateAsync(
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            throw new ArgumentException(
                "A device name is required.",
                nameof(deviceName)
            );
        }

        var devices = await _provider.GetManagedDevicesAsync(
            cancellationToken
        );

        var matches = devices
            .Where(device => string.Equals(
                device.DeviceName,
                deviceName.Trim(),
                StringComparison.OrdinalIgnoreCase
            ))
            .ToList();

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                "More than one managed device matches " +
                "the requested name. Device identity " +
                "needs to be disambiguated."
            );
        }

        return matches.SingleOrDefault();
    }
}
