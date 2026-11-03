using SupportToolkit.Core.Secrets;
using SupportToolkit.Providers.Acronis.Transport;
using SupportToolkit.Providers.BusinessCentral.Transport;
using SupportToolkit.Providers.Zendesk.Deletion;
using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Reports configuration status, and switches the active profile, as data.
/// It does the same local checks as the console's `configure status`, using
/// the same resolver and provider options, but returns view models instead
/// of printing.
///
/// Nothing is authenticated and no API is called. The resolved records
/// carry secrets, so only safe fields are copied out of them: an origin or
/// host, aliases, IDs, and whether a secret is present.
/// </summary>
public sealed class ConfigurationStatusService
{
    private const string BackupAggregatorCommand = "backup-aggregator";

    private readonly ISupportToolkitConfigurationStore _store;
    private readonly SupportToolkitConfigurationResolver _resolver;
    private readonly ISecretStore _secrets;
    private readonly Func<string, string?> _environment;

    public ConfigurationStatusService(
        ISupportToolkitConfigurationStore store,
        SupportToolkitConfigurationResolver resolver,
        ISecretStore secrets,
        Func<string, string?>? environmentReader = null)
    {
        _store =
            store ?? throw new ArgumentNullException(nameof(store));

        _resolver =
            resolver ?? throw new ArgumentNullException(nameof(resolver));

        _secrets =
            secrets ?? throw new ArgumentNullException(nameof(secrets));

        _environment =
            environmentReader ?? Environment.GetEnvironmentVariable;
    }

    public async Task<ConfigurationStatus> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var configuration =
            await _store.LoadAsync(cancellationToken);

        var active =
            FindProfile(configuration, configuration.ActiveProfile);

        return new ConfigurationStatus(
            _store.ConfigurationPath,
            active?.Name,
            configuration.Profiles
                .OrderBy(
                    pair => pair.Key,
                    StringComparer.OrdinalIgnoreCase)
                .Select(
                    pair => ToProfileStatus(
                        pair.Key,
                        pair.Value,
                        active is not null
                        && string.Equals(
                            pair.Key,
                            active.Value.Name,
                            StringComparison.OrdinalIgnoreCase)))
                .ToArray(),
            active is null
                ? null
                : await GetRuntimeAsync(
                    active.Value.Profile,
                    cancellationToken),
            await GetSavedConnectionsAsync(
                configuration,
                cancellationToken),
            RuntimeEnvironmentVariables.Overrides
                .Where(IsSet)
                .ToArray(),
            RuntimeEnvironmentVariables.Obsolete
                .Where(IsSet)
                .ToArray());
    }

    /// <summary>
    /// Makes another saved profile the active one. Nothing else in the
    /// configuration changes.
    /// </summary>
    public async Task<ProfileSwitchResult> SetActiveProfileAsync(
        string profileName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return new ProfileSwitchResult(
                false,
                "Choose a profile.");
        }

        var configuration =
            await _store.LoadAsync(cancellationToken);

        var found =
            FindProfile(configuration, profileName.Trim());

        if (found is null)
        {
            return new ProfileSwitchResult(
                false,
                "That profile does not exist.");
        }

        configuration.ActiveProfile = found.Value.Name;

        await _store.SaveAsync(configuration, cancellationToken);

        return new ProfileSwitchResult(
            true,
            $"Profile '{found.Value.Name}' is now active.");
    }

    private async Task<ActiveRuntimeStatus> GetRuntimeAsync(
        SupportToolkitProfile profile,
        CancellationToken cancellationToken)
    {
        var mode =
            await GetModeAsync(cancellationToken);

        return new ActiveRuntimeStatus(
            mode,
            await GetAcronisAsync(
                profile,
                mode.Mode == SupportToolkitMode.Production,
                cancellationToken),
            await GetZendeskTicketingAsync(profile, cancellationToken),
            await GetZendeskDeletionAsync(profile, cancellationToken),
            await GetBusinessCentralAsync(profile, cancellationToken),
            await GetReviewWaitAsync(cancellationToken));
    }

    private async Task<ModeStatus> GetModeAsync(
        CancellationToken cancellationToken)
    {
        var variable =
            SupportToolkitRuntimeOptions
                .GetEnvironmentVariableName(BackupAggregatorCommand);

        var overriddenBy = IsSet(variable) ? variable : null;

        try
        {
            return new ModeStatus(
                await _resolver.GetModuleModeAsync(
                    BackupAggregatorCommand,
                    cancellationToken),
                overriddenBy,
                null);
        }
        catch (InvalidOperationException exception)
        {
            return new ModeStatus(
                null,
                overriddenBy,
                exception.Message);
        }
    }

    private async Task<ConnectionStatus> GetAcronisAsync(
        SupportToolkitProfile profile,
        bool productionRequired,
        CancellationToken cancellationToken)
    {
        const string Name = "Acronis";

        var complete =
            IsSet("ACRONIS_DATACENTER_URL")
            && IsSet("ACRONIS_CLIENT_ID")
            && IsSet("ACRONIS_CLIENT_SECRET");

        var any =
            IsSet("ACRONIS_DATACENTER_URL")
            || IsSet("ACRONIS_CLIENT_ID")
            || IsSet("ACRONIS_CLIENT_SECRET");

        if (!complete
            && !any
            && string.IsNullOrWhiteSpace(profile.AcronisConnection))
        {
            return new ConnectionStatus(
                Name,
                ConnectionState.NotSelected,
                null,
                productionRequired
                    ? "Production backup modules require an Acronis " +
                      "connection."
                    : null,
                []);
        }

        var alias =
            complete
                ? "(environment only)"
                : profile.AcronisConnection;

        try
        {
            var resolved =
                await _resolver.GetAcronisConnectionAsync(
                    cancellationToken);

            // The same validation the production workflow applies.
            _ = AcronisOptions.Create(
                resolved.DatacenterUrl,
                resolved.ClientId,
                resolved.ClientSecret);

            return new ConnectionStatus(
                Name,
                ConnectionState.Valid,
                alias,
                null,
                [new StatusDetail("Datacenter", Origin(resolved.DatacenterUrl))]);
        }
        catch (InvalidOperationException exception)
        {
            return new ConnectionStatus(
                Name,
                ConnectionState.Invalid,
                alias,
                exception.Message,
                []);
        }
    }

    private async Task<ConnectionStatus> GetZendeskTicketingAsync(
        SupportToolkitProfile profile,
        CancellationToken cancellationToken)
    {
        const string Name = "Zendesk ticket submission";

        if (string.IsNullOrWhiteSpace(profile.ZendeskConnection))
        {
            return new ConnectionStatus(
                Name,
                ConnectionState.NotSelected,
                null,
                "Submission is unavailable until a connection and " +
                "destination are configured.",
                []);
        }

        try
        {
            var resolved =
                await _resolver.GetZendeskConnectionAsync(
                    cancellationToken);

            var options =
                ZendeskOptions.Create(
                    resolved.Subdomain,
                    resolved.ClientId,
                    resolved.ClientSecret);

            return new ConnectionStatus(
                Name,
                ConnectionState.Valid,
                profile.ZendeskConnection,
                null,
                [
                    new StatusDetail(
                        "Instance",
                        options.BaseUri.GetLeftPart(UriPartial.Authority)),
                    new StatusDetail("Destination", resolved.DestinationName),
                    new StatusDetail("Group ID", resolved.GroupId.ToString()),
                    new StatusDetail(
                        "Submitter user ID",
                        resolved.SubmitterId.ToString()),
                    new StatusDetail(
                        "Requester user ID",
                        resolved.RequesterId.ToString())
                ]);
        }
        catch (InvalidOperationException exception)
        {
            return new ConnectionStatus(
                Name,
                ConnectionState.Invalid,
                profile.ZendeskConnection,
                exception.Message,
                [
                    new StatusDetail(
                        "Destination",
                        profile.ZendeskDestination ?? "(none)")
                ]);
        }
    }

    private async Task<ConnectionStatus> GetZendeskDeletionAsync(
        SupportToolkitProfile profile,
        CancellationToken cancellationToken)
    {
        const string Name = "Zendesk deletion";

        if (string.IsNullOrWhiteSpace(profile.ZendeskDeletionConnection))
        {
            return new ConnectionStatus(
                Name,
                ConnectionState.NotSelected,
                null,
                "Bulk deletion is unavailable until a connection is " +
                "configured.",
                []);
        }

        try
        {
            var resolved =
                await _resolver.GetZendeskDeletionConnectionAsync(
                    cancellationToken);

            var options =
                ZendeskDeletionOptions.Create(
                    resolved.Subdomain,
                    resolved.ClientId);

            return new ConnectionStatus(
                Name,
                ConnectionState.Valid,
                profile.ZendeskDeletionConnection,
                null,
                [
                    new StatusDetail(
                        "Instance",
                        options.BaseUri.GetLeftPart(UriPartial.Authority)),
                    new StatusDetail(
                        "Sign-in",
                        "interactive, per run (nothing stored)")
                ]);
        }
        catch (InvalidOperationException exception)
        {
            return new ConnectionStatus(
                Name,
                ConnectionState.Invalid,
                profile.ZendeskDeletionConnection,
                exception.Message,
                []);
        }
    }

    private async Task<ConnectionStatus> GetBusinessCentralAsync(
        SupportToolkitProfile profile,
        CancellationToken cancellationToken)
    {
        const string Name = "Business Central";

        if (string.IsNullOrWhiteSpace(profile.BusinessCentralConnection))
        {
            return new ConnectionStatus(
                Name,
                ConnectionState.NotSelected,
                null,
                "The monitoring scope is unavailable until a " +
                "connection is configured.",
                []);
        }

        try
        {
            var resolved =
                await _resolver.GetBusinessCentralConnectionAsync(
                    cancellationToken);

            var options =
                BusinessCentralOptions.Create(
                    resolved.TenantId,
                    resolved.ClientId,
                    resolved.Environment,
                    resolved.CompanyId,
                    resolved.ApiPublisher,
                    resolved.ApiGroup,
                    resolved.ApiVersion,
                    resolved.CompanyName);

            return new ConnectionStatus(
                Name,
                ConnectionState.Valid,
                profile.BusinessCentralConnection,
                null,
                [
                    new StatusDetail("Environment", options.Environment),
                    new StatusDetail(
                        "API",
                        $"{options.ApiPublisher}/{options.ApiGroup}/" +
                        options.ApiVersion),
                    new StatusDetail(
                        "Sign-in",
                        "interactive, per run (nothing stored)")
                ]);
        }
        catch (InvalidOperationException exception)
        {
            return new ConnectionStatus(
                Name,
                ConnectionState.Invalid,
                profile.BusinessCentralConnection,
                exception.Message,
                []);
        }
    }

    private async Task<ReviewWaitStatus> GetReviewWaitAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var settings =
                await _resolver.GetInProgressWaitSettingsAsync(
                    BackupAggregatorCommand,
                    cancellationToken);

            return new ReviewWaitStatus(
                true,
                settings.MaxWaitSeconds == 0,
                settings.MaxWaitSeconds,
                settings.PollIntervalSeconds,
                null);
        }
        catch (InvalidOperationException exception)
        {
            return new ReviewWaitStatus(
                false,
                false,
                0,
                0,
                exception.Message);
        }
    }

    private async Task<IReadOnlyList<SavedConnectionStatus>>
        GetSavedConnectionsAsync(
            SupportToolkitConfiguration configuration,
            CancellationToken cancellationToken)
    {
        var saved = new List<SavedConnectionStatus>();

        foreach (var pair in configuration.Connections.Acronis
                     .OrderBy(
                         pair => pair.Key,
                         StringComparer.OrdinalIgnoreCase))
        {
            saved.Add(
                new SavedConnectionStatus(
                    "Acronis",
                    pair.Key,
                    [
                        new StatusDetail(
                            "Datacenter",
                            Origin(pair.Value.DatacenterUrl)),
                        new StatusDetail(
                            "Client ID",
                            Present(pair.Value.ClientId)),
                        new StatusDetail(
                            "Client secret",
                            Present(
                                await HasSecretAsync(
                                    SupportToolkitSecretKeys
                                        .AcronisClientSecret(pair.Key),
                                    cancellationToken)))
                    ]));
        }

        foreach (var pair in configuration.Connections.Zendesk
                     .OrderBy(
                         pair => pair.Key,
                         StringComparer.OrdinalIgnoreCase))
        {
            var connection = pair.Value;

            saved.Add(
                new SavedConnectionStatus(
                    "Zendesk (ticketing)",
                    pair.Key,
                    [
                        new StatusDetail(
                            "Instance",
                            Text(connection.Subdomain)),
                        new StatusDetail(
                            "Client ID",
                            Present(connection.ClientId)),
                        new StatusDetail(
                            "Client secret",
                            Present(
                                await HasSecretAsync(
                                    SupportToolkitSecretKeys
                                        .ZendeskClientSecret(pair.Key),
                                    cancellationToken))),
                        new StatusDetail(
                            "Submitter ID",
                            connection.SubmitterId?.ToString()
                            ?? "not configured"),
                        new StatusDetail(
                            "Requester ID",
                            connection.RequesterId?.ToString()
                            ?? "not configured"),
                        new StatusDetail(
                            "Destinations",
                            connection.Destinations.Count == 0
                                ? "none"
                                : string.Join(
                                    ", ",
                                    connection.Destinations.Keys
                                        .Order(
                                            StringComparer
                                                .OrdinalIgnoreCase)))
                    ]));
        }

        foreach (var pair in configuration.Connections.ZendeskDeletion
                     .OrderBy(
                         pair => pair.Key,
                         StringComparer.OrdinalIgnoreCase))
        {
            saved.Add(
                new SavedConnectionStatus(
                    "Zendesk (deletion)",
                    pair.Key,
                    [
                        new StatusDetail(
                            "Instance",
                            Text(pair.Value.Subdomain)),
                        new StatusDetail(
                            "Client ID",
                            Present(pair.Value.ClientId)),
                        new StatusDetail(
                            "Client secret",
                            "none (public client)")
                    ]));
        }

        foreach (var pair in configuration.Connections.BusinessCentral
                     .OrderBy(
                         pair => pair.Key,
                         StringComparer.OrdinalIgnoreCase))
        {
            saved.Add(
                new SavedConnectionStatus(
                    "Business Central",
                    pair.Key,
                    [
                        new StatusDetail(
                            "Environment",
                            Text(pair.Value.Environment)),
                        new StatusDetail(
                            "Client ID",
                            Present(pair.Value.ClientId))
                    ]));
        }

        return saved;
    }

    private static ProfileStatus ToProfileStatus(
        string name,
        SupportToolkitProfile profile,
        bool isActive) =>
        new(
            name,
            isActive,
            profile.AcronisConnection,
            profile.ZendeskConnection,
            profile.ZendeskDestination,
            profile.ZendeskDeletionConnection,
            profile.BusinessCentralConnection,
            new Dictionary<string, SupportToolkitMode>(
                profile.Modules,
                StringComparer.OrdinalIgnoreCase));

    private static (string Name, SupportToolkitProfile Profile)?
        FindProfile(
            SupportToolkitConfiguration configuration,
            string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (var pair in configuration.Profiles)
        {
            if (string.Equals(
                    pair.Key,
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (pair.Key, pair.Value);
            }
        }

        return null;
    }

    private async Task<bool> HasSecretAsync(
        string key,
        CancellationToken cancellationToken) =>
        !string.IsNullOrWhiteSpace(
            await _secrets.GetSecretAsync(key, cancellationToken));

    private bool IsSet(
        string variableName) =>
        !string.IsNullOrWhiteSpace(_environment(variableName));

    /// <summary>
    /// Only the origin is shown, never a path or query that might be
    /// sensitive.
    /// </summary>
    private static string Origin(
        string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
            ? uri.GetLeftPart(UriPartial.Authority)
            : "invalid or missing URL";

    private static string Present(
        string? value) =>
        Present(!string.IsNullOrWhiteSpace(value));

    private static string Present(
        bool configured) =>
        configured ? "configured" : "not configured";

    private static string Text(
        string? value) =>
        string.IsNullOrWhiteSpace(value) ? "not configured" : value;
}
