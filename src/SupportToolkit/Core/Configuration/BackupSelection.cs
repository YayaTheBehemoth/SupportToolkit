namespace SupportToolkit.Core.Configuration;

/// <summary>
/// The backup system and connection the Backup Aggregator reviews for a
/// profile, read the same way everywhere.
///
/// The profile's Backup block wins when present. Without it, the older
/// AcronisConnection selects Acronis, so configurations saved before the
/// block existed keep working unchanged. Values are as configured;
/// resolution validates them.
/// </summary>
public sealed record BackupSelection(
    string Provider,
    string? Connection)
{
    /// <summary>
    /// The backup system names Core itself needs: Acronis, which the older
    /// profile field selects. The others are registered in Composition/.
    /// </summary>
    public static class Providers
    {
        public const string Acronis = "acronis";
    }

    /// <summary>
    /// The profile's selection, or null when it selects no backup system.
    /// A Backup block without a provider yields an empty Provider, which
    /// resolution reports instead of guessing.
    /// </summary>
    public static BackupSelection? For(
        SupportToolkitProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Backup is { } block)
        {
            return new BackupSelection(
                block.Provider?.Trim().ToLowerInvariant() ?? string.Empty,
                string.IsNullOrWhiteSpace(block.Connection) ? null : block.Connection);
        }

        return string.IsNullOrWhiteSpace(profile.AcronisConnection)
            ? null
            : new BackupSelection(Providers.Acronis, profile.AcronisConnection);
    }

    public bool Is(string provider) =>
        string.Equals(Provider, provider, StringComparison.Ordinal);
}
