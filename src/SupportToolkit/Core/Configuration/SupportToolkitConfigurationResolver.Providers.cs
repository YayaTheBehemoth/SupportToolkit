using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// The active configuration as one consistent read: the file, the active
/// profile's name and the profile.
/// </summary>
public sealed record ActiveConfiguration(
    SupportToolkitConfiguration Configuration,
    string ProfileName,
    SupportToolkitProfile Profile);

/// <summary>
/// What a provider that owns its connection settings (IProviderConnections)
/// needs to resolve them the way every built-in provider does: the active
/// profile, its ticketing selection, and its secret, refused on a clash.
/// </summary>
public sealed partial class SupportToolkitConfigurationResolver
{
    /// <summary>
    /// The configuration and its active profile. Fails, as every
    /// resolution does, when no valid profile is active.
    /// </summary>
    public async Task<ActiveConfiguration> GetActiveConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration =
            await _configurationStore.LoadAsync(cancellationToken);

        var active = GetRequiredActiveProfile(configuration);

        return new ActiveConfiguration(
            configuration,
            active.Name,
            active.Profile);
    }

    /// <summary>
    /// The active profile's ticketing selection, read the same way as
    /// every resolution reads it. Null when the profile sends tickets
    /// nowhere.
    /// </summary>
    public async Task<TicketingSelection?> GetTicketingSelectionAsync(
        CancellationToken cancellationToken = default) =>
        TicketingSelection.For(
            (await GetActiveConfigurationAsync(cancellationToken)).Profile);

    /// <summary>
    /// The active profile's backup selection, read the same way as every
    /// resolution reads it. Null when the profile selects no backup system.
    /// </summary>
    public async Task<BackupSelection?> GetBackupSelectionAsync(
        CancellationToken cancellationToken = default) =>
        BackupSelection.For(
            (await GetActiveConfigurationAsync(cancellationToken)).Profile);

    /// <summary>
    /// The active profile's commercial selection, read the same way as
    /// every resolution reads it. Null when the profile selects none.
    /// </summary>
    public async Task<CommercialSelection?> GetCommercialSelectionAsync(
        CancellationToken cancellationToken = default) =>
        CommercialSelection.For(
            (await GetActiveConfigurationAsync(cancellationToken)).Profile);

    /// <summary>
    /// A provider secret, or null when none is set. Refused when another
    /// secret of the same provider is read from the same place: it would
    /// send one connection's or server's secret to another.
    /// </summary>
    public async Task<string?> ReadProviderSecretAsync(
        SupportToolkitConfiguration configuration,
        IProviderConnections provider,
        ProviderSecret secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(secret);

        var key = secret.Key;
        var location = _secretStore.ExternalLocation(key);

        if (location is not null)
        {
            var clash =
                SecretLocationClashes.For(configuration, _secretStore, provider)
                    .FirstOrDefault(item =>
                        string.Equals(item.Location, location, StringComparison.Ordinal));

            if (clash is not null)
            {
                throw new InvalidOperationException(clash.Describe());
            }
        }

        return await _secretStore.GetSecretAsync(key, cancellationToken);
    }

    /// <summary>
    /// What to do about a missing secret on this system, for a provider's
    /// own "not configured" message.
    /// </summary>
    public string DescribeMissingSecret(
        IProviderConnections provider,
        string connectionName,
        ProviderSecret secret,
        string profileName)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(secret);

        return
            $"{provider.ProviderName} {secret.Label} for connection " +
            $"'{connectionName}' is not configured. " +
            MissingSecretAdvice(secret.Key, profileName);
    }
}
