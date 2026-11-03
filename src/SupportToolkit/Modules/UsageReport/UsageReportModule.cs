using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.UsageReport.Reporting;
using SupportToolkit.Modules.UsageReport.Services;

namespace SupportToolkit.Modules.UsageReport;

/// <summary>
/// The console command for the usage report. A thin adapter: the rules
/// are in UsageReportWorkflow and UsageReportBuilder, which the web page
/// uses too.
///
/// Exit codes: 0 complete, 2 generated but incomplete (something is
/// unknown), 1 not available or invalid arguments.
/// </summary>
public sealed class UsageReportModule : ISupportToolkitModule
{
    public const string ModuleCommand = "usage-report";

    private readonly UsageReportSession _session;

    public UsageReportModule(UsageReportSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public string Command => ModuleCommand;

    public string Description =>
        "Acronis storage per customer against Business Central billing.";

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var showNames =
            args.Length == 1
            && string.Equals(args[0], "--names", StringComparison.OrdinalIgnoreCase);

        if (args.Length > 1 || (args.Length == 1 && !showNames))
        {
            PrintUsage();
            return 1;
        }

        var availability = await _session.GetAvailabilityAsync();

        if (!availability.IsAvailable)
        {
            await Console.Error.WriteLineAsync(
                "The usage report is not available: " + availability.Reason);
            return 1;
        }

        var report = await _session.GenerateAsync();

        ConsoleUsageReportReporter.Write(Console.Out, report, showNames);

        return report.IsComplete ? 0 : 2;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: SupportToolkit usage-report [--names]");
        Console.WriteLine();
        Console.WriteLine(
            "Compares Acronis storage per customer with what Business " +
            "Central bills for the backup items.");
        Console.WriteLine(
            "Read-only. Counts only by default; --names shows customer " +
            "names, amounts and one row per customer. Nothing is saved.");
    }
}
