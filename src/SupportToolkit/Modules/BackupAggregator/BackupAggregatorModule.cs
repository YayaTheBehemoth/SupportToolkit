using System.Globalization;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Activities;
using SupportToolkit.Providers.Acronis.Activities.Dtos;
using SupportToolkit.Providers.Acronis.ResourceManagement.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

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

        if (runtimeOptions.Mode
            == SupportToolkitMode.Fixture)
        {
            return await RunFixtureAsync();
        }

        if (args.Length == 0
            || args[0] is "--help" or "-h")
        {
            PrintUsage();

            return 0;
        }

        var command =
            args[0];

        /*
         * Resource Management represents current workload state rather than
         * an activity interval, so this diagnostic intentionally takes only
         * a tenant name.
         */
        if (string.Equals(
                command,
                "resource-status-probe",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            if (args.Length != 2)
            {
                PrintUsage();

                return 1;
            }

            var logger =
                new OperationalLogger();

            return await RunResourceStatusProbeAsync(
                args[1],
                logger
            );
        }

        /*
         * Activity-based commands operate against an explicit bounded
         * interval.
         */
        if (args.Length != 4)
        {
            PrintUsage();

            return 1;
        }

        var tenantName =
            args[1];

        if (!TryParseTimestamp(
                args[2],
                out var fromInclusive))
        {
            Console.Error.WriteLine(
                $"Invalid interval start: {args[2]}"
            );

            Console.Error.WriteLine(
                "Use an RFC3339 timestamp including its UTC offset."
            );

            return 1;
        }

        if (!TryParseTimestamp(
                args[3],
                out var toExclusive))
        {
            Console.Error.WriteLine(
                $"Invalid interval end: {args[3]}"
            );

            Console.Error.WriteLine(
                "Use an RFC3339 timestamp including its UTC offset."
            );

            return 1;
        }

        if (toExclusive <= fromInclusive)
        {
            Console.Error.WriteLine(
                "Interval end must be later than interval start."
            );

            return 1;
        }

        var activityLogger =
            new OperationalLogger();

        if (string.Equals(
                command,
                "review",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return await RunProductionReviewAsync(
                tenantName,
                fromInclusive,
                toExclusive,
                activityLogger
            );
        }

        if (string.Equals(
                command,
                "activity-probe",
                StringComparison.OrdinalIgnoreCase
            ))
        {
            return await RunActivityProbeAsync(
                tenantName,
                fromInclusive,
                toExclusive,
                activityLogger
            );
        }

        PrintUsage();

        return 1;
    }

    private static async Task<int> RunFixtureAsync()
    {
        var fixtureLoader =
            new BackupAggregatorFixtureLoader();

        var mixedActivities =
            await fixtureLoader
                .LoadActivitiesAsync(
                    "backup-activities-mixed.json"
                );

        var healthyActivities =
            await fixtureLoader
                .LoadActivitiesAsync(
                    "backup-activities-healthy.json"
                );

        var service =
            CreateAggregatorService();

        var tenantReports =
            new[]
            {
                service.BuildTenantReport(
                    "Fixture Tenant - Mixed",
                    mixedActivities
                ),

                service.BuildTenantReport(
                    "Fixture Tenant - Healthy",
                    healthyActivities
                )
            };

        var report =
            service.BuildAggregatedReport(
                tenantReports
            );

        var reporter =
            new ConsoleBackupAggregatorReporter();

        reporter.Write(
            report
        );

        return 0;
    }

    private static async Task<int> RunProductionReviewAsync(
        string tenantName,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        OperationalLogger logger)
    {
        logger.Info(
            "Starting single-tenant BackupAggregator review " +
            "in production mode."
        );

        var productionData =
            await FetchProductionActivitiesAsync(
                tenantName,
                fromInclusive,
                toExclusive,
                logger
            );

        if (productionData is null)
        {
            return 1;
        }

        var service =
            CreateAggregatorService();

        var tenantReport =
            service.BuildTenantReport(
                productionData.TenantName,
                productionData.Activities
            );

        var report =
            service.BuildAggregatedReport(
                new[]
                {
                    tenantReport
                }
            );

        var reporter =
            new ConsoleBackupAggregatorReporter();

        reporter.Write(
            report
        );

        return 0;
    }

    private static async Task<int> RunActivityProbeAsync(
        string tenantName,
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        OperationalLogger logger)
    {
        logger.Info(
            "Starting bounded BackupAggregator activity probe " +
            "in production mode."
        );

        var productionData =
            await FetchProductionActivitiesAsync(
                tenantName,
                fromInclusive,
                toExclusive,
                logger
            );

        if (productionData is null)
        {
            return 1;
        }

        var probe =
            new BackupAggregatorProbeService();

        return probe.ProbeActivities(
            productionData.Activities
        );
    }

    private static async Task<int> RunResourceStatusProbeAsync(
        string tenantName,
        OperationalLogger logger)
    {
        logger.Info(
            "Starting customer-scoped resource-status coverage probe."
        );

        var productionData =
            await FetchProductionResourceStatusesAsync(
                tenantName,
                logger
            );

        if (productionData is null)
        {
            return 1;
        }

        var probe =
            new BackupAggregatorResourceStatusProbeService();

        return probe.Probe(
            productionData.Resources
        );
    }

    private static async Task<ProductionActivityData?>
        FetchProductionActivitiesAsync(
            string tenantName,
            DateTimeOffset fromInclusive,
            DateTimeOffset toExclusive,
            OperationalLogger logger)
    {
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

        var tenantProvider =
            new HttpAcronisProvider(
                apiClient,
                logger
            );

        var tenant =
            await FindTenantAsync(
                tenantProvider,
                tenantName
            );

        if (tenant is null)
        {
            return null;
        }

        var activityProvider =
            new AcronisActivityProvider(
                apiClient,
                logger
            );

        var activities =
            await activityProvider
                .GetBackupActivitiesForTenantAsync(
                    tenant.Id,
                    fromInclusive,
                    toExclusive
                );

        logger.Info(
            $"Production backup activities fetched: " +
            $"{activities.Count}."
        );

        return new ProductionActivityData(
            tenant.Name,
            activities
        );
    }

    private static async Task<ProductionResourceStatusData?>
        FetchProductionResourceStatusesAsync(
            string tenantName,
            OperationalLogger logger)
    {
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

        var tenantProvider =
            new HttpAcronisProvider(
                apiClient,
                logger
            );

        var tenant =
            await FindTenantAsync(
                tenantProvider,
                tenantName
            );

        if (tenant is null)
        {
            return null;
        }

        var resources =
            await tenantProvider
                .GetResourceStatusesForTenantAsync(
                    tenant.Id
                );

        logger.Info(
            $"Customer-scoped resource statuses fetched: " +
            $"{resources.Count}."
        );

        return new ProductionResourceStatusData(
            tenant.Name,
            resources
        );
    }

    private static async Task<
        SupportToolkit.Providers.Acronis.Tenants.Dtos.TenantDto?>
        FindTenantAsync(
            HttpAcronisProvider tenantProvider,
            string tenantName)
    {
        var tenants =
            await tenantProvider
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

            return null;
        }

        if (matchingTenants.Count > 1)
        {
            Console.Error.WriteLine(
                $"More than one tenant named '{tenantName}' was found."
            );

            Console.Error.WriteLine(
                "SupportToolkit will not guess which tenant to use."
            );

            return null;
        }

        var tenant =
            matchingTenants[0];

        if (!Guid.TryParse(
                tenant.Id,
                out _))
        {
            throw new InvalidOperationException(
                "The matched tenant does not contain the UUID " +
                "required for customer-scoped authentication."
            );
        }

        return tenant;
    }

    private static BackupAggregatorService
        CreateAggregatorService()
    {
        return new BackupAggregatorService(
            new BackupActivityNormalizer(),
            new BackupActivityClassifier()
        );
    }

    private static bool TryParseTimestamp(
        string value,
        out DateTimeOffset timestamp)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out timestamp
        );
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Backup Aggregator"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Fixture review:"
        );

        Console.WriteLine(
            "  SUPPORTTOOLKIT_MODE=fixture"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Production review:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator " +
            "review <tenant-name> <from> <to>"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Production activity diagnostic:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator " +
            "activity-probe <tenant-name> <from> <to>"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Production resource-status diagnostic:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator " +
            "resource-status-probe <tenant-name>"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Activity intervals are half-open:"
        );

        Console.WriteLine(
            "  from <= startedAt < to"
        );
    }

    private sealed record ProductionActivityData(
        string TenantName,
        IReadOnlyList<AcronisActivityDto> Activities
    );

    private sealed record ProductionResourceStatusData(
        string TenantName,
        IReadOnlyList<ResourceStatusDto> Resources
    );
}