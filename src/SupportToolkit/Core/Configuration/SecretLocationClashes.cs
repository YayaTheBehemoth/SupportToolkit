using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Saved connections of one provider whose secrets are read from the same
/// place, such as one environment variable for prod-eu and prod_eu.
/// </summary>
public sealed record SecretLocationClash(
    string Provider,
    string Location,
    IReadOnlyList<string> ConnectionNames)
{
    public string Describe() =>
        $"{Provider} connections " +
        string.Join(" and ", ConnectionNames.Select(name => $"'{name}'")) +
        $" read their client secret from the same place ({Location}). " +
        "Rename one of them.";
}

/// <summary>
/// Finds secret clashes. Only a store that reads secrets from outside
/// (ExternalLocation) can have one; a store that keeps secrets itself,
/// such as the Windows Credential Manager, never does.
/// </summary>
public static class SecretLocationClashes
{
    public static IReadOnlyList<SecretLocationClash> Find(
        SupportToolkitConfiguration configuration,
        ISecretStore secrets)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(secrets);

        return
            Find(
                "Acronis",
                configuration.Connections.Acronis.Keys,
                SupportToolkitSecretKeys.AcronisClientSecret,
                secrets)
            .Concat(
                Find(
                    "Zendesk (ticketing)",
                    configuration.Connections.Zendesk.Keys,
                    SupportToolkitSecretKeys.ZendeskClientSecret,
                    secrets))
            .ToArray();
    }

    /// <summary>
    /// The clash the secret of this key is part of, or null.
    /// </summary>
    public static SecretLocationClash? For(
        SupportToolkitConfiguration configuration,
        ISecretStore secrets,
        string key)
    {
        var location = secrets.ExternalLocation(key);

        return location is null
            ? null
            : Find(configuration, secrets)
                .FirstOrDefault(
                    clash => string.Equals(
                        clash.Location,
                        location,
                        StringComparison.Ordinal));
    }

    private static IEnumerable<SecretLocationClash> Find(
        string provider,
        IEnumerable<string> connectionNames,
        Func<string, string> keyFor,
        ISecretStore secrets) =>
        connectionNames
            .Select(
                name => (
                    Name: name,
                    Location: secrets.ExternalLocation(keyFor(name))))
            .Where(item => item.Location is not null)
            .GroupBy(item => item.Location!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(
                group => new SecretLocationClash(
                    provider,
                    group.Key,
                    group
                        .Select(item => item.Name)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
            .OrderBy(clash => clash.Location, StringComparer.Ordinal);
}
