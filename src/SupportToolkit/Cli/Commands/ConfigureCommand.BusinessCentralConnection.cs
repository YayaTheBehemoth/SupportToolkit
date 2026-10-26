using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.MonitoringScope;
using SupportToolkit.Providers.BusinessCentral.Transport;

namespace SupportToolkit.Cli.Commands;

public sealed partial class ConfigureCommand
{
    /// <summary>
    /// Keeps a valid existing Business Central connection, or
    /// returns false when setup is required.
    /// </summary>
    private static bool KeepExistingBusinessCentralConnection(
        SupportToolkitConfiguration configuration,
        SupportToolkitProfile profile)
    {
        if (string.IsNullOrWhiteSpace(
                profile.BusinessCentralConnection))
        {
            return false;
        }

        var connectionName =
            profile.BusinessCentralConnection;

        var connection =
            FindBusinessCentralConnection(
                configuration,
                connectionName);

        if (connection is null)
        {
            Console.WriteLine(
                $"Business Central connection '{connectionName}' " +
                "no longer exists.");

            Console.WriteLine("Connection setup is required.");

            return false;
        }

        if (!TryCreateBusinessCentralOptions(
                connection,
                out var options,
                out _))
        {
            Console.WriteLine(
                $"Business Central connection '{connectionName}' " +
                "is incomplete or invalid.");

            Console.WriteLine("Connection setup is required.");

            return false;
        }

        Console.WriteLine("Existing Business Central configuration");
        Console.WriteLine("---------------------------------------");
        Console.WriteLine($"Connection:  {connectionName}");
        Console.WriteLine($"Environment: {options!.Environment}");
        Console.WriteLine(
            $"API:         {options.ApiPublisher}/" +
            $"{options.ApiGroup}/{options.ApiVersion}");
        Console.WriteLine("Sign-in:     interactive, per run");
        Console.WriteLine();

        var keep =
            ReadConfirmation(
                "Keep existing Business Central configuration? [Y/n]: ",
                defaultValue: true);

        if (keep)
        {
            Console.WriteLine(
                "Business Central configuration kept unchanged.");
        }

        return keep;
    }

    private static void ConfigureBusinessCentralConnection(
        SupportToolkitConfiguration configuration,
        SupportToolkitProfile profile)
    {
        Console.WriteLine("Business Central connection");
        Console.WriteLine("---------------------------");
        Console.WriteLine(
            "Read-only monitoring-scope source. Operators sign in " +
            "interactively on each run;");
        Console.WriteLine(
            "no secret or token is ever stored by SupportToolkit.");
        Console.WriteLine();

        var connectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    ReadRequiredWithDefault(
                        "Connection name",
                        GetDefaultConnectionName(
                            profile.BusinessCentralConnection,
                            configuration.Connections
                                .BusinessCentral.Keys)));

        profile.BusinessCentralConnection =
            connectionName;

        var connection =
            FindBusinessCentralConnection(
                configuration,
                connectionName)
            ?? new BusinessCentralConnectionConfiguration();

        while (true)
        {
            ConfigureBusinessCentralMetadata(connection);

            if (TryCreateBusinessCentralOptions(
                    connection,
                    out _,
                    out var problem))
            {
                break;
            }

            Console.WriteLine();
            Console.WriteLine($"Invalid value: {problem}");
            Console.WriteLine("Please re-enter the connection details.");
            Console.WriteLine();
        }

        UpsertBusinessCentralConnection(
            configuration,
            connectionName,
            connection);
    }

    /// <summary>
    /// The monitoring item number belongs to the consuming module,
    /// not to the Business Central connection.
    /// </summary>
    private static void EnsureMonitoringItemNumber(
        SupportToolkitProfile profile)
    {
        if (string.IsNullOrWhiteSpace(
                profile.BusinessCentralConnection))
        {
            return;
        }

        profile.MonitoringScope.TryGetValue(
            BackupAggregatorCommand,
            out var settings);

        settings ??=
            new ModuleMonitoringScopeConfiguration();

        Console.WriteLine();
        Console.WriteLine("Monitoring scope for Backup Aggregator");
        Console.WriteLine("--------------------------------------");

        settings.MonitoringItemNumber =
            ReadRequiredWithDefault(
                "Monitoring service item number",
                string.IsNullOrWhiteSpace(settings.MonitoringItemNumber)
                    ? SubscriptionItemNumbers
                        .DefaultMonitoringItemNumbers[0]
                    : settings.MonitoringItemNumber);

        profile.MonitoringScope[BackupAggregatorCommand] =
            settings;
    }

    private static void ConfigureBusinessCentralMetadata(
        BusinessCentralConnectionConfiguration connection)
    {
        connection.TenantId =
            ReadRequiredWithDefault(
                "Microsoft Entra tenant ID",
                connection.TenantId);

        connection.ClientId =
            ReadRequiredWithDefault(
                "App registration client ID",
                connection.ClientId);

        connection.Environment =
            ReadRequiredWithDefault(
                "Environment name",
                connection.Environment);

        connection.CompanyId =
            ReadRequiredWithDefault(
                "Company ID",
                connection.CompanyId);

        connection.CompanyName =
            ReadOptionalWithDefault(
                "Company name (optional)",
                connection.CompanyName);

        connection.ApiPublisher =
            ReadRequiredWithDefault(
                "API publisher",
                connection.ApiPublisher);

        connection.ApiGroup =
            ReadRequiredWithDefault(
                "API group",
                connection.ApiGroup);

        connection.ApiVersion =
            ReadRequiredWithDefault(
                "API version",
                connection.ApiVersion);
    }

    private static string? ReadOptionalWithDefault(
        string label,
        string? currentValue)
    {
        Console.Write(
            string.IsNullOrWhiteSpace(currentValue)
                ? $"{label}: "
                : $"{label} [{currentValue}] (type - to clear): ");

        var value =
            Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return currentValue;
        }

        return value == "-"
            ? null
            : value;
    }

    private static bool TryCreateBusinessCentralOptions(
        BusinessCentralConnectionConfiguration connection,
        out BusinessCentralOptions? options,
        out string? problem)
    {
        try
        {
            options =
                BusinessCentralOptions.Create(
                    connection.TenantId,
                    connection.ClientId,
                    connection.Environment,
                    connection.CompanyId,
                    connection.ApiPublisher,
                    connection.ApiGroup,
                    connection.ApiVersion,
                    connection.CompanyName);

            problem = null;

            return true;
        }
        catch (InvalidOperationException exception)
        {
            options = null;
            problem = exception.Message;

            return false;
        }
    }

    private static BusinessCentralConnectionConfiguration?
        FindBusinessCentralConnection(
            SupportToolkitConfiguration configuration,
            string connectionName)
    {
        return configuration.Connections.BusinessCentral
            .FirstOrDefault(
                pair =>
                    string.Equals(
                        pair.Key,
                        connectionName,
                        StringComparison.OrdinalIgnoreCase))
            .Value;
    }

    private static void UpsertBusinessCentralConnection(
        SupportToolkitConfiguration configuration,
        string connectionName,
        BusinessCentralConnectionConfiguration connection)
    {
        RemoveKeyIgnoringCase(
            configuration.Connections.BusinessCentral,
            connectionName);

        configuration.Connections.BusinessCentral[connectionName] =
            connection;
    }

    private static async Task WriteEffectiveBusinessCentralAsync(
        SupportToolkitProfile profile,
        SupportToolkitConfigurationResolver resolver)
    {
        Console.WriteLine("Business Central monitoring scope");

        if (string.IsNullOrWhiteSpace(
                profile.BusinessCentralConnection))
        {
            Console.WriteLine("  Connection: not selected");

            Console.WriteLine(
                "  Monitoring scope: unavailable until a connection " +
                "is configured.");

            return;
        }

        Console.WriteLine(
            $"  Connection alias: {profile.BusinessCentralConnection}");

        try
        {
            var resolved =
                await resolver.GetBusinessCentralConnectionAsync();

            var options =
                BusinessCentralOptions.Create(
                    resolved.TenantId,
                    resolved.ClientId,
                    resolved.Environment,
                    resolved.CompanyId,
                    resolved.ApiPublisher,
                    resolved.ApiGroup,
                    resolved.ApiVersion,
                    resolved.CompanyName);

            Console.WriteLine(
                $"  Environment: {options.Environment}");

            Console.WriteLine(
                $"  API: {options.ApiPublisher}/{options.ApiGroup}/" +
                options.ApiVersion);

            Console.WriteLine(
                "  Sign-in: interactive per run (nothing stored)");

            Console.WriteLine("  Configuration: valid locally");
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine(
                "  Configuration: incomplete or invalid");

            Console.WriteLine(
                $"  Reason: {exception.Message}");
        }
    }
}
