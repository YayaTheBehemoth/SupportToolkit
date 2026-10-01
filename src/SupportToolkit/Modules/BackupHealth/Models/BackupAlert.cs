namespace SupportToolkit.Modules.BackupHealth.Models;


/// Represents an Acronis-derived alert normalized for use inside the BackupHealth domain.
/// This model is a SupportToolkit abstraction over the raw Acronis transport DTOs and carries 
/// only the alert details needed by backup health evaluation.

public sealed class BackupAlert
{
    public required string Id { get; init; }

    public required string Type { get; init; }

    public required string Category { get; init; }

    public required string Severity { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}