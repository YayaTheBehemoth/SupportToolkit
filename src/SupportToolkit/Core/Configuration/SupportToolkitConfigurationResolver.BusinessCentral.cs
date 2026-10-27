using SupportToolkit.Core.MonitoringScope;

namespace SupportToolkit.Core.Configuration;

public sealed partial class SupportToolkitConfigurationResolver
{
    /// <summary>
    /// Resolves the active profile's Business Central connection.
    ///
    /// There are deliberately no environment overrides: the tenant,
    /// client and company that a delegated sign-in is allowed to read
    /// come only from the saved connection, so a stray environment
    /// variable cannot redirect a read to a different source.
    /// </summary>
    public async Task<ResolvedBusinessCentralConnection>
        GetBusinessCentralConnectionAsync(
            CancellationToken cancellationToken = default)
    {
        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        var active =
            GetRequiredActiveProfile(
                configuration
            );

        if (string.IsNullOrWhiteSpace(
                active.Profile.BusinessCentralConnection))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' does not reference " +
                "a Business Central connection. Configure one with " +
                $"SupportToolkit configure profile {active.Name}."
            );
        }

        var connectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    active.Profile.BusinessCentralConnection
                );

        var connection =
            configuration.Connections.BusinessCentral
                .FirstOrDefault(
                    pair =>
                        string.Equals(
                            pair.Key,
                            connectionName,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .Value;

        if (connection is null)
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' references " +
                $"Business Central connection '{connectionName}', " +
                "but that connection does not exist."
            );
        }

        string Require(string value, string field) =>
            RequireConfiguredValue(
                value,
                $"Business Central connection '{connectionName}' " +
                field
            );

        return new ResolvedBusinessCentralConnection(
            Require(connection.TenantId, "tenant ID"),
            Require(connection.ClientId, "client ID"),
            Require(connection.Environment, "environment"),
            Require(connection.CompanyId, "company ID"),
            string.IsNullOrWhiteSpace(connection.CompanyName)
                ? null
                : connection.CompanyName.Trim(),
            Require(connection.ApiPublisher, "API publisher"),
            Require(connection.ApiGroup, "API group"),
            Require(connection.ApiVersion, "API version")
        );
    }

    /// <summary>
    /// True when the active profile selects a Business Central
    /// connection. A local check only: nothing is validated against
    /// the service and no sign-in occurs.
    /// </summary>
    public async Task<bool> IsMonitoringScopeEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        if (configuration.Profiles.Count == 0)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(
            GetRequiredActiveProfile(configuration)
                .Profile.BusinessCentralConnection
        );
    }

    /// <summary>
    /// Resolves the monitoring item number a module uses to find its
    /// service in subscription lines.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetMonitoringItemNumbersAsync(
        string moduleCommand,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCommand);

        var settings =
            await GetMonitoringScopeSettingsAsync(
                moduleCommand,
                cancellationToken);

        var listed =
            (settings?.MonitoringItemNumbers ?? [])
                .Where(number => !string.IsNullOrWhiteSpace(number))
                .ToArray();

        if (listed.Length > 0)
        {
            return listed;
        }

        // Still fails closed when nothing is configured; the provisional
        // default is only offered by the configure command.
        var active =
            GetRequiredActiveProfile(
                await _configurationStore.LoadAsync(cancellationToken));

        return
        [
            RequireConfiguredValue(
                settings?.MonitoringItemNumber ?? string.Empty,
                $"Monitoring item number for '{moduleCommand}' in " +
                $"profile '{active.Name}'")
        ];
    }

    /// <summary>
    /// Items that show a customer has the underlying service, besides
    /// the entitling items. Empty when not configured.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetServiceItemNumbersAsync(
        string moduleCommand,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCommand);

        var settings =
            await GetMonitoringScopeSettingsAsync(
                moduleCommand,
                cancellationToken);

        return (settings?.ServiceItemNumbers ?? [])
            .Where(number => !string.IsNullOrWhiteSpace(number))
            .Select(number => number.Trim())
            .ToArray();
    }

    public async Task<IReadOnlySet<string>> GetExcludedTenantIdsAsync(
        string moduleCommand,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCommand);

        var settings =
            await GetMonitoringScopeSettingsAsync(
                moduleCommand,
                cancellationToken);

        return (settings?.ExcludedTenantIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<ModuleMonitoringScopeConfiguration?>
        GetMonitoringScopeSettingsAsync(
            string moduleCommand,
            CancellationToken cancellationToken)
    {
        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        return GetRequiredActiveProfile(configuration)
            .Profile.MonitoringScope
            .FirstOrDefault(
                pair =>
                    string.Equals(
                        pair.Key,
                        moduleCommand,
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .Value;
    }

    public async Task<string> GetMonitoringItemNumberAsync(
        string moduleCommand,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCommand);

        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        var active =
            GetRequiredActiveProfile(
                configuration
            );

        var settings =
            active.Profile.MonitoringScope
                .FirstOrDefault(
                    pair =>
                        string.Equals(
                            pair.Key,
                            moduleCommand,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .Value;

        return RequireConfiguredValue(
            settings?.MonitoringItemNumber ?? string.Empty,
            $"Monitoring item number for '{moduleCommand}' in " +
            $"profile '{active.Name}'"
        );
    }
}
