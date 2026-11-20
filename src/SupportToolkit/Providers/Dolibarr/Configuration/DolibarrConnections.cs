using SupportToolkit.Core.Configuration;
using SupportToolkit.Core.Secrets;
using SupportToolkit.Providers.Dolibarr.Transport;

namespace SupportToolkit.Providers.Dolibarr.Configuration;

/// <summary>
/// Non-secret Dolibarr connection settings, under connections.dolibarr in
/// the configuration file. BaseUrl is the Dolibarr root (the folder that
/// holds api/). The API key is a secret and lives in the secret store.
/// </summary>
public sealed class DolibarrConnectionConfiguration : ConfigurationNode
{
    public string BaseUrl { get; set; } =
        string.Empty;
}

/// <summary>A Dolibarr connection validated locally, with its API key.</summary>
public sealed record ResolvedDolibarrConnection(
    string ConnectionName,
    Uri ApiRoot,
    string ApiKey)
{
    /// <summary>Never shows the key.</summary>
    public override string ToString() => $"{ConnectionName} ({ApiRoot})";
}

/// <summary>
/// Dolibarr's connection settings, owned by this provider: their section
/// in the configuration file, the API key, and how the active profile's
/// commercial selection resolves against them.
/// </summary>
public sealed class DolibarrConnections : IProviderConnections
{
    /// <summary>The name a profile's commercial setting selects.</summary>
    public const string ProviderKey = "dolibarr";

    /// <summary>The configuration file section: connections.dolibarr.</summary>
    public const string Section = "dolibarr";

    public static DolibarrConnections Instance { get; } = new();

    private DolibarrConnections()
    {
    }

    public string ProviderName => "Dolibarr";

    public static ProviderSecret ApiKey(string connectionName) =>
        new(
            SupportToolkitSecretKeys.For(ProviderKey, connectionName, "api-key"),
            "API key",
            connectionName,
            "API key");

    public static IReadOnlyDictionary<string, DolibarrConnectionConfiguration> Read(
        SupportToolkitConfiguration configuration) =>
        ProviderSettings.ReadConnections<DolibarrConnectionConfiguration>(
            configuration,
            Section);

    public static void Write(
        SupportToolkitConfiguration configuration,
        IReadOnlyDictionary<string, DolibarrConnectionConfiguration> connections) =>
        ProviderSettings.WriteConnections(configuration, Section, connections);

    public IReadOnlyCollection<string> GetSavedConnectionNames(
        SupportToolkitConfiguration configuration) =>
        Read(configuration).Keys.ToArray();

    public IReadOnlyCollection<string> GetConnectionsInUse(
        SupportToolkitConfiguration configuration)
    {
        var saved = Read(configuration);

        return configuration.Profiles.Values
            .Select(CommercialSelection.For)
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
        string connectionName) =>
        [ApiKey(connectionName)];

    /// <summary>
    /// The active profile's Dolibarr connection, validated locally with its
    /// key. Nothing is called.
    /// </summary>
    public async Task<ResolvedDolibarrConnection> ResolveAsync(
        SupportToolkitConfigurationResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        var active = await resolver.GetActiveConfigurationAsync(cancellationToken);
        var profileName = active.ProfileName;

        var selection = CommercialSelection.For(active.Profile);

        if (selection is null || !selection.Is(ProviderKey))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' does not read commercial " +
                "records from Dolibarr.");
        }

        if (string.IsNullOrWhiteSpace(selection.Connection))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' selects Dolibarr but " +
                "names no connection (commercial.connection).");
        }

        string connectionName;

        try
        {
            connectionName =
                SupportToolkitConfigurationNames.NormalizeConnectionName(selection.Connection);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"'{selection.Connection}' is not a valid connection name.",
                exception);
        }

        if (!Read(active.Configuration).TryGetValue(connectionName, out var connection))
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' references Dolibarr " +
                $"connection '{connectionName}', but that connection does " +
                "not exist.");
        }

        var apiRoot = DolibarrOptions.ParseApiRoot(connection.BaseUrl, connectionName);

        var secret = ApiKey(connectionName);

        var apiKey =
            await resolver.ReadProviderSecretAsync(
                active.Configuration,
                this,
                secret,
                cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                resolver.DescribeMissingSecret(this, connectionName, secret, profileName));
        }

        if (apiKey.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new InvalidOperationException(
                $"The Dolibarr API key of connection '{connectionName}' " +
                "contains spaces or control characters.");
        }

        return new ResolvedDolibarrConnection(connectionName, apiRoot, apiKey);
    }
}
