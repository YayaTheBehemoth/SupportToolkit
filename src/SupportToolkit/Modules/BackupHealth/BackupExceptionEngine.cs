using SupportToolkit.Modules.BackupHealth.Models;

namespace SupportToolkit.Modules.BackupHealth;

public sealed class BackupExceptionEngine
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _staleAfter;

    public BackupExceptionEngine(
        TimeProvider timeProvider,
        TimeSpan staleAfter)
    {
        _timeProvider = timeProvider;
        _staleAfter = staleAfter;
    }

    public IReadOnlyList<BackupException> Evaluate(
        IEnumerable<BackupResource> resources)
    {
        var exceptions = new List<BackupException>();

        foreach (var resource in resources)
        {
            var exception = EvaluateResource(resource);

            if (exception is not null)
            {
                exceptions.Add(exception);
            }
        }

        return exceptions;
    }

    private BackupException? EvaluateResource(
        BackupResource resource)
    {
        var reasons = new List<string>();

        var severity = BackupExceptionSeverity.Warning;
        var hasException = false;

        if (resource.Status.Equals(
                "critical",
                StringComparison.OrdinalIgnoreCase))
        {
            hasException = true;
            severity = BackupExceptionSeverity.Critical;

            reasons.Add(
                "Acronis reports the resource status as critical."
            );
        }
        else if (resource.Status.Equals(
                     "error",
                     StringComparison.OrdinalIgnoreCase))
        {
            hasException = true;
            severity = BackupExceptionSeverity.Critical;

            reasons.Add(
                "Acronis reports the resource status as error."
            );
        }
        else if (resource.Status.Equals(
                     "warning",
                     StringComparison.OrdinalIgnoreCase))
        {
            hasException = true;

            reasons.Add(
                "Acronis reports the resource status as warning."
            );
        }
        else if (resource.Status.Equals(
                     "no_policies_applied",
                     StringComparison.OrdinalIgnoreCase))
        {
            hasException = true;

            reasons.Add(
                "No protection policies are applied to the resource."
            );
        }

        foreach (var alert in resource.Alerts)
        {
            if (alert.Severity.Equals(
                    "critical",
                    StringComparison.OrdinalIgnoreCase))
            {
                hasException = true;
                severity = BackupExceptionSeverity.Critical;

                reasons.Add(
                    $"Critical Acronis alert: {alert.Type}."
                );
            }
            else if (alert.Severity.Equals(
                         "warning",
                         StringComparison.OrdinalIgnoreCase))
            {
                hasException = true;

                reasons.Add(
                    $"Warning Acronis alert: {alert.Type}."
                );
            }
        }

        if (resource.LastSuccessfulBackup is not null)
        {
            var now = _timeProvider.GetUtcNow();

            var backupAge =
                now - resource.LastSuccessfulBackup.Value;

            if (backupAge > _staleAfter)
            {
                hasException = true;

                reasons.Add(
                    $"Last successful backup is {FormatAge(backupAge)} old."
                );
            }
        }

        if (!hasException)
        {
            return null;
        }

        return new BackupException
        {
            TenantName = resource.TenantName,
            ResourceId = resource.ResourceId,
            ResourceName = resource.ResourceName,
            Severity = severity,
            Reasons = reasons.AsReadOnly(),
            LastSuccessfulBackup =
                resource.LastSuccessfulBackup
        };
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalDays >= 1)
        {
            return $"{age.TotalDays:F1} days";
        }

        return $"{age.TotalHours:F1} hours";
    }
}