using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Modules.BackupHealth.Models;

namespace SupportToolkit.Tests;

public class BackupExceptionEngineTests
{
    private readonly BackupExceptionEngine _engine = new();

    [Fact]
    public void Evaluate_HealthyResource_IsSuppressed()
    {
        var resource = CreateResource(
            status: "idle"
        );

        var exceptions = _engine.Evaluate(
            new[] { resource }
        );

        Assert.Empty(exceptions);
    }

    [Fact]
    public void Evaluate_ErrorStatus_CreatesCriticalException()
    {
        var resource = CreateResource(
            status: "error"
        );

        var exception = Assert.Single(
            _engine.Evaluate(new[] { resource })
        );

        Assert.Equal(
            BackupExceptionSeverity.Critical,
            exception.Severity
        );

        Assert.Contains(
            exception.Reasons,
            reason => reason.Contains("status as error")
        );
    }

    [Fact]
    public void Evaluate_CriticalAlert_CreatesCriticalException()
    {
        var resource = CreateResource(
            status: "idle",
            alerts:
            [
                new BackupAlert
                {
                    Id = "alert-001",
                    Type = "BackupFailed",
                    Category = "Backup",
                    Severity = "critical",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        );

        var exception = Assert.Single(
            _engine.Evaluate(new[] { resource })
        );

        Assert.Equal(
            BackupExceptionSeverity.Critical,
            exception.Severity
        );

        Assert.Contains(
            exception.Reasons,
            reason => reason.Contains("BackupFailed")
        );
    }

    [Fact]
    public void Evaluate_NoPoliciesApplied_CreatesWarningException()
    {
        var resource = CreateResource(
            status: "no_policies_applied"
        );

        var exception = Assert.Single(
            _engine.Evaluate(new[] { resource })
        );

        Assert.Equal(
            BackupExceptionSeverity.Warning,
            exception.Severity
        );
    }

    [Fact]
    public void Evaluate_MultipleSignals_PreservesAllReasons()
    {
        var resource = CreateResource(
            status: "error",
            alerts:
            [
                new BackupAlert
                {
                    Id = "alert-001",
                    Type = "BackupFailed",
                    Category = "Backup",
                    Severity = "critical",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        );

        var exception = Assert.Single(
            _engine.Evaluate(new[] { resource })
        );

        Assert.Equal(2, exception.Reasons.Count);

        Assert.Contains(
            exception.Reasons,
            reason => reason.Contains("status as error")
        );

        Assert.Contains(
            exception.Reasons,
            reason => reason.Contains("BackupFailed")
        );
    }

    private static BackupResource CreateResource(
        string status,
        IReadOnlyList<BackupAlert>? alerts = null)
    {
        return new BackupResource
        {
            TenantId =
                "11111111-aaaa-aaaa-aaaa-111111111111",

            TenantName = "Customer Alpha",

            ResourceId =
                "11111111-1111-1111-1111-111111111111",

            ResourceName = "BACKUP-SERVER-01",

            ResourceType = "resource.machine",

            Status = status,

            LastSuccessfulBackup =
                DateTimeOffset.UtcNow.AddHours(-8),

            Alerts =
                alerts ?? Array.Empty<BackupAlert>()
        };
    }
}