
namespace SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;

public interface IGraphManagedDeviceProvider
{
    Task<IReadOnlyList<ManagedDevice>> GetManagedDevicesAsync(
        CancellationToken cancellationToken = default
    );
}
