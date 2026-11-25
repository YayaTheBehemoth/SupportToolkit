using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Providers.Zammad.Configuration;

/// <summary>
/// Zammad's connection settings, owned by this provider: their section in
/// the configuration file, their secret, and how the active profile's
/// selection resolves against them.
/// </summary>
public sealed class ZammadConnections : IProviderConnections
{
    /// <summary>The name a profile's ticketing setting selects.</summary>
    public const string ProviderKey = "zammad";

    /// <summary>The configuration file section: connections.zammad.</summary>
    public const string Section = "zammad";

    public static ZammadConnections Instance { get; } = new();

    private ZammadConnections()
    {
    }

    public string ProviderName => "Zammad";

    /// <summary>The connection's one secret, its API token.</summary>
    public static ProviderSecret ApiToken(string connectionName) =>
        new(
            SupportToolkitSecretKeys.For(ProviderKey, connectionName, "api-token"),
            "API token",
            connectionName,
            "API token");

    public IReadOnlyList<ProviderSecret> GetSecrets(
        SupportToolkitConfiguration configuration,
        string connectionName) =>
        [ApiToken(connectionName)];

    public static IReadOnlyDictionary<string, ZammadConnectionConfiguration> Read(
        SupportToolkitConfiguration configuration) =>
        ProviderSettings.ReadConnections<ZammadConnectionConfiguration>(
            configuration,
            Section);

    public static void Write(
        SupportToolkitConfiguration configuration,
        IReadOnlyDictionary<string, ZammadConnectionConfiguration> connections) =>
        ProviderSettings.WriteConnections(configuration, Section, connections);

    public IReadOnlyCollection<string> GetSavedConnectionNames(
        SupportToolkitConfiguration configuration) =>
        Read(configuration).Keys.ToArray();

    public IReadOnlyCollection<string> GetConnectionsInUse(
        SupportToolkitConfiguration configuration)
    {
        var saved = Read(configuration);

        return configuration.Profiles.Values
            .Select(TicketingSelection.For)
            .Where(selection =>
                selection is not null
                && selection.Is(ProviderKey)
                && !string.IsNullOrWhiteSpace(selection.Connection))
            .Select(selection => selection!.Connection!.Trim())
            .Where(saved.ContainsKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// The active profile's Zammad connection and destination, validated
    /// locally. Nothing is called and the token is not checked against
    /// Zammad. Like every destination, it requires an active profile that
    /// selects it explicitly.
    /// </summary>
    public async Task<ResolvedZammadConnection> ResolveAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var active = await resolver.GetActiveConfigurationAsync(cancellationToken);
        var profileName = active.ProfileName;

        var selection = TicketingSelection.For(active.Profile);

        if (selection is null || !selection.Is(ProviderKey))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' does not send tickets " +
                "to Zammad.");
        }

        if (string.IsNullOrWhiteSpace(selection.Connection))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' selects Zammad but " +
                "names no connection (ticketing.connection).");
        }

        var connectionName = Normalize(selection.Connection);

        if (!Read(active.Configuration).TryGetValue(connectionName, out var connection))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' references Zammad " +
                $"connection '{connectionName}', but that connection " +
                "does not exist.");
        }

        if (string.IsNullOrWhiteSpace(selection.Destination))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' does not select " +
                "a Zammad destination (ticketing.destination). " +
                "Ticket submission requires an explicitly configured " +
                "destination.");
        }

        var destinationName = Normalize(selection.Destination);

        var destination =
            connection.Destinations
                .FirstOrDefault(pair =>
                    string.Equals(
                        pair.Key,
                        destinationName,
                        StringComparison.OrdinalIgnoreCase))
                .Value;

        if (destination is null)
        {
            throw new InvalidOperationException(
                $"Zammad destination '{destinationName}' does not " +
                $"exist on connection '{connectionName}'.");
        }

        if (string.IsNullOrWhiteSpace(destination.Group))
        {
            throw new InvalidOperationException(
                $"Zammad destination '{destinationName}' names no group. " +
                "A group name is required.");
        }

        var baseUrl = Require(connection.BaseUrl, connectionName, "baseUrl");
        var customer = Require(connection.Customer, connectionName, "customer");

        var secret = ApiToken(connectionName);

        var apiToken =
            await resolver.ReadProviderSecretAsync(
                active.Configuration,
                this,
                secret,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(apiToken))
        {
            throw new InvalidOperationException(
                resolver.DescribeMissingSecret(this, connectionName, secret, profileName));
        }

        return new ResolvedZammadConnection(
            connectionName,
            baseUrl,
            apiToken,
            customer,
            destinationName,
            destination.Group.Trim());
    }

    private static string Normalize(string name)
    {
        try
        {
            return SupportToolkitConfigurationNames.NormalizeConnectionName(name);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"'{name}' is not a valid connection or destination name.",
                exception);
        }
    }

    private static string Require(
        string value,
        string connectionName,
        string setting) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Zammad connection '{connectionName}' {setting} " +
                "is not configured.")
            : value.Trim();
}
