using SupportToolkit.Modules.BackupHealth.Models;

namespace SupportToolkit.Reporting;


/// Writes BackupHealth results to the console.
/// This reporter is a presentation layer for the BackupHealth domain and deliberately has no knowledge of Acronis APIs, DTOs, authentication,
/// or transport concerns. That separation allows other reporters, such as HTML or JSON output, to be added later without changing exception evaluation.

public sealed class ConsoleBackupHealthReporter
{
    public void Write(
        IReadOnlyList<BackupResource> resources,
        IReadOnlyList<BackupException> exceptions)
    {
        var criticalCount = exceptions.Count(
            exception =>
                exception.Severity
                    == BackupExceptionSeverity.Critical
        );

        var warningCount = exceptions.Count(
            exception =>
                exception.Severity
                    == BackupExceptionSeverity.Warning
        );

        var suppressedCount =
            resources.Count - exceptions.Count;

        Console.WriteLine();
        Console.WriteLine("SupportToolkit - Backup Health");
        Console.WriteLine("==============================");
        Console.WriteLine();

        Console.WriteLine($"Resources scanned:    {resources.Count}");
        Console.WriteLine($"Healthy suppressed:   {suppressedCount}");
        Console.WriteLine($"Exceptions:           {exceptions.Count}");
        Console.WriteLine($"  Critical:           {criticalCount}");
        Console.WriteLine($"  Warning:            {warningCount}");
        Console.WriteLine();

        if (exceptions.Count == 0)
        {
            Console.WriteLine("No backup exceptions found.");
            return;
        }

        foreach (var tenantGroup in exceptions
                     .GroupBy(exception => exception.TenantName)
                     .OrderBy(group => group.Key))
        {
            Console.WriteLine(tenantGroup.Key);
            Console.WriteLine(
                new string('-', tenantGroup.Key.Length)
            );

            foreach (var exception in tenantGroup
                         .OrderByDescending(exception =>
                             exception.Severity)
                         .ThenBy(exception =>
                             exception.ResourceName))
            {
                Console.WriteLine(
                    $"[{exception.Severity.ToString().ToUpperInvariant()}] " +
                    exception.ResourceName
                );

                if (exception.LastSuccessfulBackup is not null)
                {
                    Console.WriteLine(
                        $"  Last successful backup: " +
                        $"{exception.LastSuccessfulBackup:u}"
                    );
                }

                foreach (var reason in exception.Reasons)
                {
                    Console.WriteLine(
                        $"  - {reason}"
                    );
                }

                Console.WriteLine();
            }
        }
    }
}