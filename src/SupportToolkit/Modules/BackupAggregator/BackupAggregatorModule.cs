using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Modules.BackupAggregator;

public sealed class BackupAggregatorModule
    : ISupportToolkitModule
{
    public string Command =>
        "backup-aggregator";

    public string Description =>
        "Aggregate backup reporting across Acronis tenants.";

    public async Task<int> RunAsync(
        string[] args)
    {
        var runtimeOptions =
            SupportToolkitRuntimeOptions.FromEnvironment();

        if (args.Length == 0
            || args[0] is "--help" or "-h")
        {
            PrintUsage();

            return 0;
        }

        var logger =
            new OperationalLogger();

        logger.Info(
            $"Starting {Command} in " +
            $"{runtimeOptions.Mode.ToString().ToLowerInvariant()} mode."
        );

        return runtimeOptions.Mode switch
        {
            SupportToolkitMode.Fixture =>
                await RunFixtureAsync(
                    args
                ),

            SupportToolkitMode.Production =>
                await RunProductionAsync(
                    args,
                    logger
                ),

            _ =>
                throw new InvalidOperationException(
                    $"Unsupported SupportToolkit mode: " +
                    $"{runtimeOptions.Mode}."
                )
        };
    }

    private static async Task<int> RunFixtureAsync(
        string[] args)
    {
        var fixtureDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Acronis"
            );

        var provider =
            new FixtureAcronisProvider(
                fixtureDirectory
            );

        var resources =
            await provider
                .GetResourceStatusesAsync();

        var probe =
            new BackupAggregatorProbeService();

        if (args.Length == 1
            && string.Equals(
                args[0],
                "list",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return probe.List(
                resources
            );
        }

        if (args.Length == 2
            && string.Equals(
                args[0],
                "probe",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return probe.Probe(
                resources,
                args[1]
            );
        }

        PrintUsage();

        return 1;
    }

    private static async Task<int> RunProductionAsync(
        string[] args,
        OperationalLogger logger)
    {
        if (args.Length < 2)
        {
            PrintUsage();

            return 1;
        }

        var operation =
            args[0];

        var tenantName =
            args[1];

        var acronisOptions =
            AcronisOptions.FromEnvironment();

        using var innerHandler =
            new HttpClientHandler
            {
                AllowAutoRedirect =
                    false
            };

        using var readOnlyHandler =
            new AcronisReadOnlyHandler(
                acronisOptions.DatacenterUrl,
                innerHandler
            );

        using var httpClient =
            new HttpClient(
                readOnlyHandler
            )
            {
                Timeout =
                    TimeSpan.FromSeconds(30)
            };

        var apiClient =
            new AcronisApiClient(
                httpClient,
                acronisOptions,
                logger:
                    logger
            );

        var provider =
            new HttpAcronisProvider(
                apiClient,
                logger
            );

        var tenants =
            await provider
                .GetTenantsAsync();

        var matchingTenants =
            tenants
                .Where(
                    tenant =>
                        string.Equals(
                            tenant.Name,
                            tenantName,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .ToList();

        if (matchingTenants.Count == 0)
        {
            Console.Error.WriteLine(
                $"No tenant named '{tenantName}' was found."
            );

            return 1;
        }

        if (matchingTenants.Count > 1)
        {
            Console.Error.WriteLine(
                $"More than one tenant named '{tenantName}' was found."
            );

            Console.Error.WriteLine(
                "The tool will not guess which tenant to use."
            );

            return 1;
        }

        var tenant =
            matchingTenants[0];

        logger.Info(
            "Matching customer tenant resolved."
        );

        var resources =
            await provider
                .GetResourceStatusesForTenantAsync(
                    tenant.Id
                );

        var probe =
            new BackupAggregatorProbeService();

        if (string.Equals(
                operation,
                "list",
                StringComparison.OrdinalIgnoreCase)
            && args.Length == 2)
        {
            return probe.List(
                resources
            );
        }

        if (string.Equals(
                operation,
                "probe",
                StringComparison.OrdinalIgnoreCase)
            && args.Length == 3)
        {
            return probe.Probe(
                resources,
                args[2]
            );
        }

        PrintUsage();

        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Backup Aggregator"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Fixture mode:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator list"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator probe <resource-name>"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Production mode:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator list <tenant-name>"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator " +
            "probe <tenant-name> <resource-name>"
        );
    }
}