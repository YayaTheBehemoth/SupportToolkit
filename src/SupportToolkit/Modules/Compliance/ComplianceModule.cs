
using System.Net;
using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.Compliance.Services;
using SupportToolkit.Providers.MicrosoftGraph.ManagedDevices;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Modules.Compliance;

/// <summary>
/// Dev Proxy-only compliance investigation.
///
/// No real Graph authentication, production connections,
/// remediation or ticket writes are supported.
/// </summary>
public sealed class ComplianceModule : ISupportToolkitModule
{
    public const string ModuleCommand = "compliance";

    public string Command => ModuleCommand;

    public string Description =>
        "Investigate synthetic Intune compliance evidence.";

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0
            && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        if (args.Length != 2
            || !string.Equals(
                args[0],
                "investigate",
                StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 1;
        }

        if (!args[1].StartsWith(
            "STK-SYNTHETIC-",
            StringComparison.OrdinalIgnoreCase))
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

        var workflow = new ComplianceEvidenceInvestigationService(
            new ComplianceInvestigationService(deviceProvider),
            settingProvider
        );

        Console.WriteLine();
        Console.WriteLine("Compliance investigation (Dev Proxy)");
        Console.WriteLine(
            "Source: synthetic Microsoft Graph responses"
        );
        Console.WriteLine();

        var result = await workflow.InvestigateAsync(
            args[1]
        );

        if (result is null)
        {
            Console.WriteLine(
                "Device not found in simulated inventory."
            );
            Console.WriteLine(
                "This does not establish compliance."
            );
            return 2;
        }

        var device = result.Device;

        if (device.DeviceName is null
            || !device.DeviceName.StartsWith(
                "STK-SYNTHETIC-",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Expected synthetic device data."
            );
        }

        Console.WriteLine(
            $"Device:           {device.DeviceName}"
        );
        Console.WriteLine(
            $"Graph device ID:  {device.Id}"
        );
        Console.WriteLine(
            $"Operating system: {device.OperatingSystem ?? "not provided"}"
        );
        Console.WriteLine(
            $"Compliance state: {device.ComplianceState ?? "not provided"}"
        );
        Console.WriteLine(
            $"Last check-in:    {FormatDate(device.LastSyncDateTime)}"
        );

        Console.WriteLine();
        Console.WriteLine("Candidate setting evidence:");

        if (result.MatchedSettings.Count == 0)
        {
            Console.WriteLine(
                "  No setting states matched the device ID."
            );
            Console.WriteLine(
                "  Compliance cause remains unknown."
            );
        }
        else
        {
            foreach (var setting in result.MatchedSettings)
            {
                Console.WriteLine(
                    $"  [{setting.State ?? "unknown"}] " +
                    setting.SettingName
                );
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            "Evidence correlation: SYNTHETIC / UNVERIFIED"
        );
        Console.WriteLine(
            "Device-ID relationships require live validation."
        );
        Console.WriteLine(
            "No remediation or case-status decision was made."
        );
        Console.WriteLine();
        Console.WriteLine(
            "PASS: synthetic evidence investigation completed."
        );

        return 0;
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value.HasValue
            ? value.Value.UtcDateTime.ToString(
                "yyyy-MM-dd HH:mm:ss 'UTC'"
            )
            : "not provided";
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Compliance (development only)"
        );
        Console.WriteLine();

        Console.WriteLine(
            "  SupportToolkit compliance investigate <synthetic-device-name>"
        );

        Console.WriteLine();
        Console.WriteLine(
            "Requires local Dev Proxy on 127.0.0.1:8000."
        );
        Console.WriteLine(
            "No live Microsoft Graph authentication."
        );
    }
}
