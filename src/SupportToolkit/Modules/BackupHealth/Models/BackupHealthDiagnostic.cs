namespace SupportToolkit.Modules.BackupHealth.Models;

/// <summary>
/// Describes data that could not be cleanly normalized into the BackupHealth
/// domain without failing the entire scan.
/// </summary>
public sealed class BackupHealthDiagnostic
{
    public required BackupHealthDiagnosticKind Kind { get; init; }

    public required string Message { get; init; }
}

public enum BackupHealthDiagnosticKind
{
    ResourceMissingId,
    ResourceMissingTenantId,
    ResourceUnknownTenant,
    AlertMissingResourceId,
    AlertUnmatchedResource
}