namespace SupportToolkit.Core.Configuration;

/// <summary>
/// The commercial system that supplies monitoring entitlement and customer
/// names for a profile, read the same way everywhere.
///
/// The profile's Commercial block wins when present. Without it, the older
/// BusinessCentralConnection selects Business Central, so configurations
/// saved before the block existed keep working unchanged. Values are as
/// configured; resolution validates them.
/// </summary>
public sealed record CommercialSelection(
    string Provider,
    string? Connection)
{
    /// <summary>
    /// The commercial system names Core itself needs: Business Central,
    /// which the older profile field selects. The others are registered in
    /// Composition/.
    /// </summary>
    public static class Providers
    {
        public const string BusinessCentral = "businesscentral";
    }

    /// <summary>
    /// The profile's selection, or null when it selects none. A Commercial
    /// block without a provider yields an empty Provider, which resolution
    /// reports instead of guessing.
    /// </summary>
    public static CommercialSelection? For(
        SupportToolkitProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Commercial is { } block)
        {
            return new CommercialSelection(
                block.Provider?.Trim().ToLowerInvariant() ?? string.Empty,
                string.IsNullOrWhiteSpace(block.Connection) ? null : block.Connection);
        }

        return string.IsNullOrWhiteSpace(profile.BusinessCentralConnection)
            ? null
            : new CommercialSelection(
                Providers.BusinessCentral,
                profile.BusinessCentralConnection);
    }

    public bool Is(string provider) =>
        string.Equals(Provider, provider, StringComparison.Ordinal);
}
