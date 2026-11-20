using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Providers.OsTicket.Configuration;

/// <summary>
/// osTicket's connection settings, owned by this provider: their section
/// in the configuration file, their secret, and how the active profile's
/// selection resolves against them.
/// </summary>
public sealed class OsTicketConnections : IProviderConnections
{
    /// <summary>The name a profile's ticketing setting selects.</summary>
    public const string ProviderKey = "osticket";

    /// <summary>The configuration file section: connections.osTicket.</summary>
    public const string Section = "osTicket";

    public static OsTicketConnections Instance { get; } = new();

    private OsTicketConnections()
    {
    }

    public string ProviderName => "osTicket";

    public string SecretName => "API key";

    public string GetSecretKey(string connectionName) =>
        SupportToolkitSecretKeys.For(ProviderKey, connectionName, "api-key");

    public static IReadOnlyDictionary<string, OsTicketConnectionConfiguration> Read(
        SupportToolkitConfiguration configuration) =>
        ProviderSettings.ReadConnections<OsTicketConnectionConfiguration>(
            configuration,
            Section);

    public static void Write(
        SupportToolkitConfiguration configuration,
        IReadOnlyDictionary<string, OsTicketConnectionConfiguration> connections) =>
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
    /// The active profile's osTicket connection and destination, validated
    /// locally. Nothing is called and the key is not checked against
    /// osTicket. Like every destination, it requires an active profile
    /// that selects it explicitly.
    /// </summary>
    public async Task<ResolvedOsTicketConnection> ResolveAsync(
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
                "to osTicket.");
        }

        if (string.IsNullOrWhiteSpace(selection.Connection))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' selects osTicket but " +
                "names no connection (ticketing.connection).");
        }

        var connectionName = Normalize(selection.Connection);

        if (!Read(active.Configuration).TryGetValue(connectionName, out var connection))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' references osTicket " +
                $"connection '{connectionName}', but that connection " +
                "does not exist.");
        }

        if (string.IsNullOrWhiteSpace(selection.Destination))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' does not select " +
                "an osTicket destination (ticketing.destination). " +
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
                $"osTicket destination '{destinationName}' does not " +
                $"exist on connection '{connectionName}'.");
        }

        if (destination.TopicId <= 0)
        {
            throw new InvalidOperationException(
                $"osTicket destination '{destinationName}' has an " +
                "invalid help topic ID. A positive topicId is required.");
        }

        var baseUrl = Require(connection.BaseUrl, connectionName, "baseUrl");
        var requesterName = Require(connection.RequesterName, connectionName, "requesterName");
        var requesterEmail = Require(connection.RequesterEmail, connectionName, "requesterEmail");

        var apiKey =
            await resolver.ReadProviderSecretAsync(
                active.Configuration,
                this,
                connectionName,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                resolver.DescribeMissingSecret(this, connectionName, profileName));
        }

        return new ResolvedOsTicketConnection(
            connectionName,
            baseUrl,
            apiKey,
            requesterName,
            requesterEmail,
            destinationName,
            destination.TopicId);
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
                $"osTicket connection '{connectionName}' {setting} " +
                "is not configured.")
            : value.Trim();
}
