namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Configuration status as data, for any interface to render. It is checked
/// locally: nothing is authenticated and no API is called. It contains no
/// secret, only whether one is present.
/// </summary>
public sealed record ConfigurationStatus(
    string ConfigurationPath,
    string? ActiveProfile,
    IReadOnlyList<ProfileStatus> Profiles,
    ActiveRuntimeStatus? Runtime,
    IReadOnlyList<SavedConnectionStatus> SavedConnections,
    IReadOnlyList<string> EnvironmentOverridesSet,
    IReadOnlyList<string> ObsoleteEnvironmentVariablesSet)
{
    public bool HasProfiles => Profiles.Count > 0;
}

public sealed record ProfileStatus(
    string Name,
    bool IsActive,
    string? AcronisConnection,
    string? ZendeskConnection,
    string? ZendeskDestination,
    string? ZendeskDeletionConnection,
    string? BusinessCentralConnection,
    IReadOnlyDictionary<string, SupportToolkitMode> Modules);

/// <summary>
/// The effective runtime of the active profile.
/// </summary>
public sealed record ActiveRuntimeStatus(
    ModeStatus BackupAggregatorMode,
    ConnectionStatus Acronis,
    ConnectionStatus ZendeskTicketing,
    ConnectionStatus ZendeskDeletion,
    ConnectionStatus BusinessCentral,
    ReviewWaitStatus ReviewWait);

/// <summary>
/// Mode is null when the configuration is invalid (Problem says why).
/// OverriddenBy names the environment variable that overrides the profile,
/// if one is set.
/// </summary>
public sealed record ModeStatus(
    SupportToolkitMode? Mode,
    string? OverriddenBy,
    string? Problem);

public enum ConnectionState
{
    /// <summary>The active profile does not use this connection.</summary>
    NotSelected,

    /// <summary>It resolves and validates locally.</summary>
    Valid,

    /// <summary>It is selected but incomplete or invalid; see Reason.</summary>
    Invalid
}

public sealed record StatusDetail(
    string Label,
    string Value);

public sealed record ConnectionStatus(
    string Name,
    ConnectionState State,
    string? Alias,
    string? Reason,
    IReadOnlyList<StatusDetail> Details);

public sealed record ReviewWaitStatus(
    bool IsValid,
    bool IsDisabled,
    int MaxWaitSeconds,
    int PollIntervalSeconds,
    string? Problem);

public sealed record SavedConnectionStatus(
    string Kind,
    string Name,
    IReadOnlyList<StatusDetail> Details);

public sealed record ProfileSwitchResult(
    bool Succeeded,
    string Message);
