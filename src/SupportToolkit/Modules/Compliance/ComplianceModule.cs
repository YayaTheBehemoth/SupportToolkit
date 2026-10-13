
using System.Net;
using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.Compliance.Reporting;
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Modules.Compliance;

/// <summary>
/// Dev Proxy-only compliance investigation and inventory.
///
/// No production Graph authentication, remediation,
/// or ticket writes are supported.
/// </summary>
public sealed class ComplianceModule : ISupportToolkitModule
{
    public const string ModuleCommand = "compliance";

    public string Command => ModuleCommand;

    public string Description =>
        "Review synthetic Intune compliance evidence.";

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0
            && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var listCommand =
            args.Length == 1
            && string.Equals(
                args[0],
                "list",
                StringComparison.OrdinalIgnoreCase
            );

        var investigateCommand =
            args.Length is 2 or 3
            && string.Equals(
                args[0],
                "investigate",
                StringComparison.OrdinalIgnoreCase
            );

        var notePreview =
            args.Length == 3
            && string.Equals(
                args[2],
                "--note-preview",
                StringComparison.OrdinalIgnoreCase
            );

        if (!listCommand
            && (!investigateCommand
                || (args.Length == 3 && !notePreview)))
        {
            PrintUsage();
            return 1;
        }

        if (investigateCommand
            && !IsSyntheticName(args[1]))
        {
            throw new ArgumentException(
                "This development command accepts " +
                "synthetic device names only."
            );
        }

        using var handler = new HttpClientHandler
        {
            UseProxy = true,
            Proxy = new WebProxy(
                "http://127.0.0.1:8000"
            ),
            UseDefaultCredentials = false,
            UseCookies = false
        };

        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var deviceProvider = new GraphManagedDeviceProvider(
            httpClient
        );

        var settingProvider = new GraphComplianceSettingProvider(
            httpClient
        );

        if (listCommand)
        {
            return await RunListAsync(
                deviceProvider,
                settingProvider
            );
        }

        var workflow = new ComplianceEvidenceInvestigationService(
            new ComplianceInvestigationService(deviceProvider),
            settingProvider
        );

        var investigation = await workflow.InvestigateAsync(
            args[1]
        );

        if (investigation is null)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Device not found in simulated inventory."
            );
            Console.WriteLine(
                "This does not establish compliance."
            );
            return 2;
        }

        ValidateSyntheticDevice(investigation.Device);

        var report = new ComplianceInvestigationReportFactory()
            .Create(investigation);

        var reporter = new ComplianceInvestigationReporter();

        Console.WriteLine();
        Console.Write(reporter.Render(report, notePreview));
        Console.WriteLine();

        if (!notePreview)
        {
            Console.WriteLine(
                "PASS: synthetic investigation report generated."
            );
        }

        return 0;
    }

    private static async Task<int> RunListAsync(
        IGraphManagedDeviceProvider deviceProvider,
        IGraphComplianceSettingProvider settingProvider)
    {
        var service = new ComplianceDeviceListService(
            deviceProvider,
            settingProvider
        );

        var result = await service.GetAsync();

        // Reject unexpected data before displaying it.
        foreach (var entry in result.Entries)
        {
            ValidateSyntheticDevice(entry.Device);
        }

        var reporter = new ComplianceDeviceListReporter();

        Console.WriteLine();
        Console.Write(reporter.Render(result));
        Console.WriteLine();

        return 0;
    }

    private static void ValidateSyntheticDevice(
        ManagedDevice device)
    {
        if (!IsSyntheticName(device.DeviceName))
        {
            throw new InvalidDataException(
                "Expected synthetic Dev Proxy device data."
            );
        }
    }

    private static bool IsSyntheticName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name)
            && name.StartsWith(
                "STK-SYNTHETIC-",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Compliance (development only)");
        Console.WriteLine();

        Console.WriteLine("Device inventory:");
        Console.WriteLine(
            "  SupportToolkit compliance list"
        );

        Console.WriteLine();

        Console.WriteLine("Device investigation:");
        Console.WriteLine(
            "  SupportToolkit compliance investigate <synthetic-device-name>"
        );
        Console.WriteLine(
            "  SupportToolkit compliance investigate <synthetic-device-name> --note-preview"
        );

        Console.WriteLine();
        Console.WriteLine(
            "Requires local Dev Proxy on 127.0.0.1:8000."
        );
        Console.WriteLine(
            "No live Graph authentication or ticket writes."
        );
    }
}
