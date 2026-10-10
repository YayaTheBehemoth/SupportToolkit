using SupportToolkit.Core.Modules;
using SupportToolkit.Modules.BackupHealth.Models;
using SupportToolkit.Modules.BackupHealth.Reporting;
using SupportToolkit.Modules.BackupHealth.Services;

namespace SupportToolkit.Modules.BackupHealth;

public sealed class BackupHealthModule
    : ISupportToolkitModule
{
    public const string ModuleCommand =
        "backup-health";

    private readonly BackupHealthWorkflow
        _workflow;

    public string Command =>
        ModuleCommand;

    public string Description =>
        "Evaluate backup health across Acronis tenants.";

    public BackupHealthModule(
        BackupHealthWorkflow workflow)
    {
        _workflow =
            workflow
            ?? throw new ArgumentNullException(
                nameof(workflow)
            );
    }

    public async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length > 0
            && args[0] is "--help" or "-h")
        {
            PrintUsage();

            return 0;
        }

        if (args.Length > 0)
        {
            PrintUsage();

            return 1;
        }

        var result =
            await _workflow
                .EvaluateAsync();

        WriteResult(
            result
        );

        return 0;
    }

    private static void WriteResult(
        BackupHealthResult result)
    {
        new ConsoleBackupHealthReporter()
            .Write(
                result.Snapshot.Resources,
                result.Exceptions,
                result.Snapshot.Diagnostics
            );
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Backup Health"
        );

        Console.WriteLine();

        Console.WriteLine(
            "Run backup health evaluation:"
        );

        Console.WriteLine(
            "  SupportToolkit backup-health"
        );

        Console.WriteLine();

        Console.WriteLine(
            "The active SupportToolkit profile controls whether " +
            "BackupHealth uses fixture or production data."
        );

        Console.WriteLine();

        Console.WriteLine(
            "Optional runtime override:"
        );

        Console.WriteLine(
            "  set SUPPORTTOOLKIT_BACKUP_HEALTH_MODE=fixture"
        );

        Console.WriteLine(
            "  set SUPPORTTOOLKIT_BACKUP_HEALTH_MODE=production"
        );
    }
}