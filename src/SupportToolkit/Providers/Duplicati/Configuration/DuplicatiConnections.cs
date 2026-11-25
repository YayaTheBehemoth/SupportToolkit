using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Providers.Duplicati.Transport;

namespace SupportToolkit.Providers.Duplicati.Configuration;

/// <summary>
/// Duplicati's connection settings, owned by this provider: their section
/// in the configuration file, one password per server, and how the active
/// profile's backup selection resolves against them.
/// </summary>
public sealed class DuplicatiConnections : IProviderConnections
{
    /// <summary>The name a profile's backup setting selects.</summary>
    public const string ProviderKey = "duplicati";

    /// <summary>The configuration file section: connections.duplicati.</summary>
    public const string Section = "duplicati";

    public static DuplicatiConnections Instance { get; } = new();

    private DuplicatiConnections()
    {
    }

    public string ProviderName => "Duplicati";

    /// <summary>The password of one server of a fleet connection.</summary>
    public static ProviderSecret ServerPassword(
        string connectionName,
        string serverAlias)
    {
        var alias = Normalize(serverAlias, "server alias");

        return new ProviderSecret(
            SupportToolkitSecretKeys.For(
                ProviderKey,
                connectionName,
                "password-" + alias),
            "password",
            $"{connectionName}, server {alias}",
            $"password of server '{alias}'");
    }

    public static IReadOnlyDictionary<string, DuplicatiConnectionConfiguration> Read(
        SupportToolkitConfiguration configuration) =>
        ProviderSettings.ReadConnections<DuplicatiConnectionConfiguration>(
            configuration,
            Section);

    public static void Write(
        SupportToolkitConfiguration configuration,
        IReadOnlyDictionary<string, DuplicatiConnectionConfiguration> connections) =>
        ProviderSettings.WriteConnections(configuration, Section, connections);

    public IReadOnlyCollection<string> GetSavedConnectionNames(
        SupportToolkitConfiguration configuration) =>
        Read(configuration).Keys.ToArray();

    public IReadOnlyCollection<string> GetConnectionsInUse(
        SupportToolkitConfiguration configuration)
    {
        var saved = Read(configuration);

        return configuration.Profiles.Values
            .Select(BackupSelection.For)
            .Where(selection =>
                selection is not null
                && selection.Is(ProviderKey)
                && !string.IsNullOrWhiteSpace(selection.Connection))
            .Select(selection => selection!.Connection!.Trim())
            .Where(saved.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ProviderSecret> GetSecrets(
        SupportToolkitConfiguration configuration,
        string connectionName)
    {
        if (!Read(configuration).TryGetValue(connectionName, out var fleet))
        {
            return [];
        }

        return fleet.Customers.Values
            .SelectMany(customer => customer.Servers.Keys)
            // An invalid alias is reported when the fleet is resolved;
            // status and clash checks only list the usable ones.
            .Where(IsValidName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(alias => ServerPassword(connectionName, alias))
            .ToArray();
    }

    /// <summary>
    /// The active profile's Duplicati fleet, validated locally with every
    /// server's password. Nothing is called. Any missing or invalid
    /// setting fails the whole fleet: a review never silently skips a
    /// server.
    /// </summary>
    public async Task<ResolvedDuplicatiFleet> ResolveAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var active = await resolver.GetActiveConfigurationAsync(cancellationToken);
        var profileName = active.ProfileName;

        var selection = BackupSelection.For(active.Profile);

        if (selection is null || !selection.Is(ProviderKey))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' does not review Duplicati " +
                "backups.");
        }

        if (string.IsNullOrWhiteSpace(selection.Connection))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' selects Duplicati but " +
                "names no connection (backup.connection).");
        }

        var connectionName = Normalize(selection.Connection, "connection name");

        if (!Read(active.Configuration).TryGetValue(connectionName, out var fleet))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' references Duplicati " +
                $"connection '{connectionName}', but that connection does " +
                "not exist.");
        }

        if (fleet.Customers.Count == 0)
        {
            throw new InvalidOperationException(
                $"Duplicati connection '{connectionName}' lists no customers.");
        }

        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customers = new List<ResolvedDuplicatiCustomer>();

        foreach (var (customerName, customer) in fleet.Customers
                     .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(customerName))
            {
                throw new InvalidOperationException(
                    $"Duplicati connection '{connectionName}' has a customer " +
                    "without a name.");
            }

            if (customer.Servers.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Duplicati customer '{customerName.Trim()}' lists no servers.");
            }

            var servers = new List<ResolvedDuplicatiServer>();

            foreach (var (serverAlias, server) in customer.Servers
                         .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                var alias = Normalize(serverAlias, "server alias");

                // One password per alias: two servers sharing an alias would
                // share a password.
                if (!seenAliases.Add(alias))
                {
                    throw new InvalidOperationException(
                        $"Duplicati connection '{connectionName}' uses server " +
                        $"alias '{alias}' more than once. Each server needs " +
                        "its own alias.");
                }

                var baseUri = DuplicatiOptions.ParseServerUrl(server.Url, alias);

                var secret = ServerPassword(connectionName, alias);

                var password =
                    await resolver.ReadProviderSecretAsync(
                        active.Configuration,
                        this,
                        secret,
                        cancellationToken);

                if (string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException(
                        resolver.DescribeMissingSecret(
                            this,
                            connectionName,
                            secret,
                            profileName));
                }

                servers.Add(new ResolvedDuplicatiServer(alias, baseUri, password));
            }

            customers.Add(new ResolvedDuplicatiCustomer(customerName.Trim(), servers));
        }

        return new ResolvedDuplicatiFleet(connectionName, customers);
    }

    private static bool IsValidName(string name)
    {
        try
        {
            SupportToolkitConfigurationNames.NormalizeConnectionName(name);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string Normalize(string name, string what)
    {
        try
        {
            return SupportToolkitConfigurationNames.NormalizeConnectionName(name);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"'{name}' is not a valid Duplicati {what}. Use letters, " +
                "digits, '-', '_' or '.'.",
                exception);
        }
    }
}
