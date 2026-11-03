using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Modules.BackupAggregator.Services;

/// <summary>
/// Whether the monitored review can be offered, and in which mode.
/// Fixture mode is always available. Production needs a Business
/// Central connection in the active profile; Reason says what is
/// missing when it is not available.
/// </summary>
public sealed record ScopedReviewAvailability(
    SupportToolkitMode Mode,
    bool IsAvailable,
    string? Reason);
