using System.Globalization;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Logging;
using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.BackupAggregator.Reporting;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Activities;
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

        /*
         * Fixture mode exercises the actual aggregation pipeline
         * without making any production API calls.
         */
        if (runtimeOptions.Mode
            == SupportToolkitMode.Fixture)
        {
            return await RunFixtureAsync();
        }

        /*
         * Production mode is still intentionally limited to the
         * explicit single-tenant activity probe.
         *
         * The MSP-wide production run will be added later.
         */
        if (args.Length == 0
            || args[0] is "--help" or "-h")
        {
            PrintUsage();

            return 0;
        }

        if (args.Length != 4
            || !string.Equals(
                args[0],
                "activity-probe",
                StringComparison.OrdinalIgnoreCase
            ))
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

        var logger =
            new OperationalLogger();

        logger.Info(
            "Starting backup-aggregator bounded activity probe " +
            "in production mode."
        );

        return await RunActivityProbeAsync(
            tenantName,
            fromInclusive,
            toExclusive,
            logger
        );
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

        var normalizer =
            new BackupActivityNormalizer();

        var classifier =
            new BackupActivityClassifier();

        var service =
            new BackupAggregatorService(
                normalizer,
                classifier
            );

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

    private static async Task<int> RunActivityProbeAsync(
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

            return 1;
        }

        if (matchingTenants.Count > 1)
        {
            Console.Error.WriteLine(
                $"More than one tenant named '{tenantName}' was found."
            );

            Console.Error.WriteLine(
                "The probe will not guess which tenant to use."
            );

            return 1;
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

        logger.Info(
            "Matching customer tenant resolved."
        );

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

        var probe =
            new BackupAggregatorProbeService();

        return probe.ProbeActivities(
            activities
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
            "Fixture mode:"
        );

        Console.WriteLine(
            "  SUPPORTTOOLKIT_MODE=fixture"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Production diagnostic:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator " +
            "activity-probe <tenant-name> <from> <to>"
        );

        Console.WriteLine();

        Console.WriteLine(
            "The production probe interval is half-open:"
        );

        Console.WriteLine(
            "  from <= startedAt < to"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Use RFC3339 timestamps with an explicit UTC offset."
        );

        Console.WriteLine();

        Console.WriteLine(
            "Example:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-aggregator activity-probe " +
            "\"Customer Name\" " +
            "\"2026-10-06T00:00:00+02:00\" " +
            "\"2026-10-07T00:00:00+02:00\""
        );
    }
}