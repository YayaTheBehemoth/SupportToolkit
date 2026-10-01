using SupportToolkit.Modules.BackupHealth.Models;

namespace SupportToolkit.Reporting;

/// <summary>
/// Writes BackupHealth results to the console.
///
/// This reporter is a presentation boundary and deliberately has no knowledge
/// of Acronis HTTP, authentication, DTOs, or transport behavior.
/// </summary>
public sealed class ConsoleBackupHealthReporter
{
    public void Write(
        IReadOnlyList<BackupResource> resources,
        IReadOnlyList<BackupException> exceptions,
        IReadOnlyList<BackupHealthDiagnostic> diagnostics)
    {
        var criticalCount =
            exceptions.Count(
                exception =>
                    exception.Severity
                    == BackupExceptionSeverity.Critical
            );

        var warningCount =
            exceptions.Count(
                exception =>
                    exception.Severity
                    == BackupExceptionSeverity.Warning
            );

        var suppressedCount =
            resources.Count - exceptions.Count;

        Console.WriteLine();
        Console.WriteLine(
            "SupportToolkit - Backup Health"
        );
        Console.WriteLine(
            "=============================="
        );
        Console.WriteLine();

        Console.WriteLine(
            $"Resources scanned:    {resources.Count}"
        );

        Console.WriteLine(
            $"Healthy suppressed:   {suppressedCount}"
        );

        Console.WriteLine(
            $"Exceptions:           {exceptions.Count}"
        );

        Console.WriteLine(
            $"  Critical:           {criticalCount}"
        );

        Console.WriteLine(
            $"  Warning:            {warningCount}"
        );

        Console.WriteLine(
            $"Diagnostics:          {diagnostics.Count}"
        );

        Console.WriteLine();

        if (exceptions.Count == 0)
        {
            Console.WriteLine(
                "No backup exceptions found."
            );

            Console.WriteLine();
        }
        else
        {
            WriteExceptions(exceptions);
        }

        if (diagnostics.Count > 0)
        {
            WriteDiagnostics(diagnostics);
        }
    }

    private static void WriteExceptions(
        IReadOnlyList<BackupException> exceptions)
    {
        foreach (var tenantGroup in exceptions
                     .GroupBy(
                         exception =>
                             exception.TenantName
                     )
                     .OrderBy(
                         group =>
                             group.Key
                     ))
        {
            Console.WriteLine(
                tenantGroup.Key
            );

            Console.WriteLine(
                new string(
                    '-',
                    tenantGroup.Key.Length
                )
            );

            foreach (var exception in tenantGroup
                         .OrderByDescending(
                             exception =>
                                 exception.Severity
                         )
                         .ThenBy(
                             exception =>
                                 exception.ResourceName
                         ))
            {
                Console.WriteLine(
                    $"[{exception.Severity.ToString().ToUpperInvariant()}] " +
                    exception.ResourceName
                );

                if (exception.LastSuccessfulBackup
                    is not null)
                {
                    Console.WriteLine(
                        $"  Last successful backup: " +
                        $"{exception.LastSuccessfulBackup:u}"
                    );
                }

                foreach (var reason
                         in exception.Reasons)
                {
                    Console.WriteLine(
                        $"  - {reason}"
                    );
                }

                Console.WriteLine();
            }
        }
    }

    private static void WriteDiagnostics(
        IReadOnlyList<BackupHealthDiagnostic> diagnostics)
    {
        Console.WriteLine(
            "Data diagnostics"
        );

        Console.WriteLine(
            "----------------"
        );

        foreach (var diagnostic in diagnostics)
        {
            Console.WriteLine(
                $"[{diagnostic.Kind}] " +
                diagnostic.Message
            );
        }

        Console.WriteLine();
    }
}