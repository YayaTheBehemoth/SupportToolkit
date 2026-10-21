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
