using System.Globalization;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Core.Reconciliation;

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

    /// <summary>
    /// Reads the module's acknowledged exceptions.
    ///
    /// An entry that cannot be interpreted is not applied and is
    /// returned as a notice instead, so a typo can never hide a
    /// discrepancy. A local check only.
    /// </summary>
    public async Task<AcknowledgementSettings> GetAcknowledgementsAsync(
        string moduleCommand,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCommand);

        var settings =
            await GetMonitoringScopeSettingsAsync(
                moduleCommand,
                cancellationToken);

        var acknowledgements =
            new List<ExceptionAcknowledgement>();

        var notices =
            new List<string>();

        var entries =
            settings?.AcknowledgedExceptions ?? [];

        for (var index = 0; index < entries.Count; index++)
        {
            var entry =
                entries[index];

            var problem =
                entry is null
                    ? "is empty"
                    : string.IsNullOrWhiteSpace(entry.TenantId)
                        ? "has no tenant ID"
                        : !Enum.TryParse<DiscrepancyType>(
                            entry.DiscrepancyType?.Trim(),
                            ignoreCase: true,
                            out _)
                          || int.TryParse(entry.DiscrepancyType, out _)
                            ? "has an unknown discrepancy type"
                            : string.IsNullOrWhiteSpace(entry.Reason)
                                ? "has no reason"
                                : !DateOnly.TryParseExact(
                                    entry.ExpiresOn?.Trim(),
                                    "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.None,
                                    out _)
                                    ? "has no valid expiry date (yyyy-MM-dd)"
                                    : null;

            if (problem is not null)
            {
                notices.Add(
                    $"Acknowledged exception {index + 1} {problem} " +
                    "and was not applied.");

                continue;
            }

            acknowledgements.Add(
                new ExceptionAcknowledgement(
                    entry!.TenantId.Trim(),
                    Enum.Parse<DiscrepancyType>(
                        entry.DiscrepancyType.Trim(),
                        ignoreCase: true),
                    entry.Reason.Trim(),
                    DateOnly.ParseExact(
                        entry.ExpiresOn.Trim(),
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    string.IsNullOrWhiteSpace(entry.AcknowledgedBy)
                        ? null
                        : entry.AcknowledgedBy.Trim()));
        }

        return new AcknowledgementSettings(
            acknowledgements,
            notices);
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
