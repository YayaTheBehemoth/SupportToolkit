using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// A secret a connection needs that is not stored on this computer.
/// SecretName says what it is ("client secret", "API key").
/// </summary>
public sealed record MissingSecret(
    string Provider,
    string ConnectionName,
    string Key,
    string SecretName = "client secret");

/// <summary>
/// Which secrets a configuration needs that this computer does not have.
///
/// Only connections that a profile actually uses count; a saved connection
/// nothing refers to needs nothing. Acronis and Zendesk ticketing have a
/// client secret; a provider that owns its settings names its own. Business Central and Zendesk deletion sign in
/// interactively and store none.
///
/// It reports which secrets are missing and never reads a value out.
/// </summary>
public static class RequiredSecrets
{
    public static Task<IReadOnlyList<MissingSecret>> FindMissingAsync(
        SupportToolkitConfiguration configuration,
        ISecretStore secrets,
        CancellationToken cancellationToken = default) =>
        FindMissingAsync(configuration, secrets, [], cancellationToken);

    /// <param name="providers">
    /// Providers that own their connection settings (registered in
    /// Composition/), checked after the built-in ones.
    /// </param>
    public static async Task<IReadOnlyList<MissingSecret>> FindMissingAsync(
        SupportToolkitConfiguration configuration,
        ISecretStore secrets,
        IEnumerable<IProviderConnections> providers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providers);

        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secrets);

        var missing = new List<MissingSecret>();

        var acronis =
            configuration.Profiles.Values
                .Select(profile => profile.AcronisConnection)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => configuration.Connections.Acronis.ContainsKey(name))
                .Order(StringComparer.OrdinalIgnoreCase);

        foreach (var name in acronis)
        {
            await AddIfMissingAsync(
                missing,
                secrets,
                "Acronis",
                name,
                SupportToolkitSecretKeys.AcronisClientSecret(name),
                cancellationToken);
        }

        var zendesk =
            configuration.Profiles.Values
                .Select(TicketingSelection.For)
                .Where(selection =>
                    selection is not null
                    && selection.Is(TicketingSelection.Providers.Zendesk)
                    && !string.IsNullOrWhiteSpace(selection.Connection))
                .Select(selection => selection!.Connection!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(name => configuration.Connections.Zendesk.ContainsKey(name))
                .Order(StringComparer.OrdinalIgnoreCase);

        foreach (var name in zendesk)
        {
            await AddIfMissingAsync(
                missing,
                secrets,
                "Zendesk (ticketing)",
                name,
                SupportToolkitSecretKeys.ZendeskClientSecret(name),
                cancellationToken);
        }

        foreach (var provider in providers)
        {
            var inUse =
                provider.GetConnectionsInUse(configuration)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase);

            foreach (var name in inUse)
            {
                await AddIfMissingAsync(
                    missing,
                    secrets,
                    provider.ProviderName,
                    name,
                    provider.GetSecretKey(name),
                    cancellationToken,
                    provider.SecretName);
            }
        }

        return missing;
    }

    private static async Task AddIfMissingAsync(
        List<MissingSecret> missing,
        ISecretStore secrets,
        string provider,
        string connectionName,
        string key,
        CancellationToken cancellationToken,
        string secretName = "client secret")
    {
        var value = await secrets.GetSecretAsync(key, cancellationToken);

        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(
                new MissingSecret(provider, connectionName, key, secretName));
        }
    }
}
