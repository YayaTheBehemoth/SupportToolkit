using SupportToolkit.Core.Configuration;
using SupportToolkit.Providers.OsTicket.Configuration;

namespace SupportToolkit.Composition;

/// <summary>
/// Every provider that owns its connection settings. Required secrets,
/// secret clashes and status cover each one listed here; a new provider of
/// that kind is one entry.
/// </summary>
public static class ProviderConnectionsComposition
{
    public static IReadOnlyList<IProviderConnections> All { get; } =
    [
        OsTicketConnections.Instance
    ];
}
