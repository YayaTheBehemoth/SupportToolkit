using SupportToolkit.Core.Configuration;
using SupportToolkit.Modules.BackupAggregator;
using SupportToolkit.Providers.Dolibarr.Configuration;
using SupportToolkit.Providers.Duplicati.Configuration;
using SupportToolkit.Providers.OsTicket.Configuration;

namespace SupportToolkit.Composition;

/// <summary>
/// The "lab" profile: SupportToolkit against the second-provider lab that
/// lab/up.sh starts (osTicket, Duplicati, Dolibarr), on the same loopback
/// ports wherever it runs: in the codespace, or on another computer with
/// those ports forwarded. Everything here must match lab/up.sh.
///
/// It owns the profile named "lab" and the connections named "lab" of
/// those three providers; applying it again resets them. Nothing else in
/// the configuration is touched, and secrets are not part of it.
/// </summary>
public static class LabProfileComposition
{
    public const string ProfileName = "lab";

    public const string ConnectionName = "lab";

    public const string MonitoringItem = "EXAMPLE-MONITORING";

    public const string BackupItem = "EXAMPLE-BACKUP";

    /// <summary>
    /// Adds or resets the lab profile and its connections. Returns true
    /// when the profile already existed.
    /// </summary>
    public static bool Apply(SupportToolkitConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var osTicket = new Dictionary<string, OsTicketConnectionConfiguration>(OsTicketConnections.Read(configuration))
        {
            [ConnectionName] = new()
            {
                BaseUrl = "http://127.0.0.1:8080/",
                RequesterName = "Example Requester",
                RequesterEmail = "requester@example.com",
                Destinations = { ["backups"] = new OsTicketDestinationConfiguration { TopicId = 10 } }
            }
        };

        var duplicati = new Dictionary<string, DuplicatiConnectionConfiguration>(DuplicatiConnections.Read(configuration))
        {
            [ConnectionName] = new()
            {
                Customers =
                {
                    // Named like the lab's Dolibarr customers, so the
                    // monitored review can match them.
                    ["Northwind Traders"] = new()
                    {
                        Servers = { ["machine-a"] = new() { Url = "http://127.0.0.1:8201/" } }
                    },
                    ["Contoso"] = new()
                    {
                        Servers = { ["machine-b"] = new() { Url = "http://127.0.0.1:8202/" } }
                    }
                }
            }
        };

        var dolibarr = new Dictionary<string, DolibarrConnectionConfiguration>(DolibarrConnections.Read(configuration))
        {
            [ConnectionName] = new() { BaseUrl = "http://127.0.0.1:8090/" }
        };

        OsTicketConnections.Write(configuration, osTicket);
        DuplicatiConnections.Write(configuration, duplicati);
        DolibarrConnections.Write(configuration, dolibarr);

        var existing =
            configuration.Profiles.Keys.FirstOrDefault(name =>
                string.Equals(name, ProfileName, StringComparison.OrdinalIgnoreCase));

        configuration.Profiles[existing ?? ProfileName] =
            new SupportToolkitProfile
            {
                Ticketing = new() { Provider = OsTicketConnections.ProviderKey, Connection = ConnectionName, Destination = "backups" },
                Backup = new() { Provider = DuplicatiConnections.ProviderKey, Connection = ConnectionName },
                Commercial = new() { Provider = DolibarrConnections.ProviderKey, Connection = ConnectionName },
                Modules = { [BackupAggregatorModule.ModuleCommand] = SupportToolkitMode.Production },
                MonitoringScope =
                {
                    [BackupAggregatorModule.ModuleCommand] = new ModuleMonitoringScopeConfiguration
                    {
                        MonitoringItemNumber = MonitoringItem,
                        ServiceItemNumbers = [BackupItem]
                    }
                }
            };

        return existing is not null;
    }

    /// <summary>Whether a missing secret belongs to the lab's connections.</summary>
    public static bool IsLabSecret(MissingSecret secret) =>
        string.Equals(secret.ConnectionName, ConnectionName, StringComparison.OrdinalIgnoreCase)
        && (secret.Provider == OsTicketConnections.Instance.ProviderName
            || secret.Provider == DuplicatiConnections.Instance.ProviderName
            || secret.Provider == DolibarrConnections.Instance.ProviderName);
}
