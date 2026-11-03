using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// A client secret a connection needs that is not stored on this computer.
/// </summary>
public sealed record MissingSecret(
    string Provider,
    string ConnectionName,
    string Key);

/// <summary>
/// Which secrets a configuration needs that this computer does not have.
///
/// Only connections that a profile actually uses count; a saved connection
/// nothing refers to needs nothing. Only Acronis and Zendesk ticketing have
/// a client secret. Business Central and Zendesk deletion sign in
/// interactively and store none.
///
/// It reports which secrets are missing and never reads a value out.
/// </summary>
public static class RequiredSecrets
{
    public static async Task<IReadOnlyList<MissingSecret>> FindMissingAsync(
        SupportToolkitConfiguration configuration,
        ISecretStore secrets,
        CancellationToken cancellationToken = default)
    {
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
                .Select(profile => profile.ZendeskConnection)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
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

        return missing;
    }

    private static async Task AddIfMissingAsync(
        List<MissingSecret> missing,
        ISecretStore secrets,
        string provider,
        string connectionName,
        string key,
        CancellationToken cancellationToken)
    {
        var value = await secrets.GetSecretAsync(key, cancellationToken);

        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add(new MissingSecret(provider, connectionName, key));
        }
    }
}
