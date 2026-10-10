using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Resolves effective runtime configuration for SupportToolkit.
///
/// Resolution order:
///
/// Module modes:
/// 1. Environment override
/// 2. Active profile
/// 3. Fixture default
///
/// External writes:
/// 1. SUPPORTTOOLKIT_ALLOW_WRITES environment override
/// 2. Active profile
/// 3. Disabled
///
/// Provider configuration:
/// 1. Environment override for each configured value
/// 2. Active profile's named connection
///
/// Secrets:
/// 1. Environment override
/// 2. ISecretStore for the named connection
/// </summary>
public sealed class SupportToolkitConfigurationResolver
{
    private readonly ISupportToolkitConfigurationStore
        _configurationStore;

    private readonly ISecretStore
        _secretStore;

    private readonly Func<string, string?>
        _environmentReader;

    public SupportToolkitConfigurationResolver(
        ISupportToolkitConfigurationStore configurationStore,
        ISecretStore secretStore,
        Func<string, string?>? environmentReader = null)
    {
        _configurationStore =
            configurationStore
            ?? throw new ArgumentNullException(
                nameof(configurationStore)
            );

        _secretStore =
            secretStore
            ?? throw new ArgumentNullException(
                nameof(secretStore)
            );

        _environmentReader =
            environmentReader
            ?? Environment.GetEnvironmentVariable;
    }

    public async Task<SupportToolkitMode> GetModuleModeAsync(
        string moduleCommand,
        CancellationToken cancellationToken = default)
    {
        var environmentVariable =
            SupportToolkitRuntimeOptions
                .GetEnvironmentVariableName(
                    moduleCommand
                );

        var environmentValue =
            _environmentReader(
                environmentVariable
            );

        if (!string.IsNullOrWhiteSpace(
                environmentValue))
        {
            return SupportToolkitRuntimeOptions
                .FromEnvironment(
                    moduleCommand,
                    _environmentReader
                )
                .Mode;
        }

        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        if (configuration.Profiles.Count == 0)
        {
            return SupportToolkitMode.Fixture;
        }

        var active =
            GetRequiredActiveProfile(
                configuration
            );

        var configuredMode =
            active.Profile.Modules
                .FirstOrDefault(
                    pair =>
                        string.Equals(
                            pair.Key,
                            moduleCommand,
                            StringComparison.OrdinalIgnoreCase
                        )
                );

        return string.IsNullOrWhiteSpace(
                configuredMode.Key)
            ? SupportToolkitMode.Fixture
            : configuredMode.Value;
    }

    public async Task<bool> GetAllowWritesAsync(
        CancellationToken cancellationToken = default)
    {
        var environmentValue =
            ReadEnvironmentValue(
                "SUPPORTTOOLKIT_ALLOW_WRITES"
            );

        if (environmentValue is not null)
        {
            if (bool.TryParse(
                    environmentValue,
                    out var allowWrites))
            {
                return allowWrites;
            }

            throw new InvalidOperationException(
                "SUPPORTTOOLKIT_ALLOW_WRITES must be either " +
                "'true' or 'false'."
            );
        }

        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        if (configuration.Profiles.Count == 0)
        {
            return false;
        }

        var active =
            GetRequiredActiveProfile(
                configuration
            );

        return active.Profile.AllowWrites;
    }

    public async Task<ResolvedAcronisConnection>
        GetAcronisConnectionAsync(
            CancellationToken cancellationToken = default)
    {
        var environmentDatacenterUrl =
            ReadEnvironmentValue(
                "ACRONIS_DATACENTER_URL"
            );

        var environmentClientId =
            ReadEnvironmentValue(
                "ACRONIS_CLIENT_ID"
            );

        var environmentClientSecret =
            ReadEnvironmentValue(
                "ACRONIS_CLIENT_SECRET"
            );

        /*
         * Preserve the original completely environment-driven workflow.
         *
         * If all three values are supplied explicitly, SupportToolkit does
         * not require a profile or persistent connection at all.
         */
        if (environmentDatacenterUrl is not null
            && environmentClientId is not null
            && environmentClientSecret is not null)
        {
            return new ResolvedAcronisConnection(
                environmentDatacenterUrl,
                environmentClientId,
                environmentClientSecret
            );
        }

        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        var active =
            GetRequiredActiveProfile(
                configuration
            );

        if (string.IsNullOrWhiteSpace(
                active.Profile.AcronisConnection))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' does not reference " +
                "an Acronis connection. Configure one with " +
                $"SupportToolkit configure profile {active.Name}, or " +
                "provide all ACRONIS_* environment variables."
            );
        }

        var connectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    active.Profile.AcronisConnection
                );

        var connection =
            FindAcronisConnection(
                configuration,
                connectionName
            );

        if (connection is null)
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' references Acronis " +
                $"connection '{connectionName}', but that connection " +
                "does not exist."
            );
        }

        var datacenterUrl =
            environmentDatacenterUrl
            ?? RequireConfiguredValue(
                connection.DatacenterUrl,
                $"Acronis connection '{connectionName}' datacenter URL"
            );

        var clientId =
            environmentClientId
            ?? RequireConfiguredValue(
                connection.ClientId,
                $"Acronis connection '{connectionName}' client ID"
            );

        var clientSecret =
            environmentClientSecret
            ?? await _secretStore.GetSecretAsync(
                SupportToolkitSecretKeys
                    .AcronisClientSecret(
                        connectionName
                    ),
                cancellationToken
            );

        if (string.IsNullOrWhiteSpace(
                clientSecret))
        {
            throw new InvalidOperationException(
                $"Acronis client secret for connection " +
                $"'{connectionName}' is not configured. Run " +
                $"SupportToolkit configure profile {active.Name} " +
                "or provide ACRONIS_CLIENT_SECRET."
            );
        }

        return new ResolvedAcronisConnection(
            datacenterUrl,
            clientId,
            clientSecret
        );
    }

    public async Task<ResolvedZendeskConnection>
        GetZendeskConnectionAsync(
            CancellationToken cancellationToken = default)
    {
        var environmentSubdomain =
            ReadEnvironmentValue(
                "ZENDESK_SUBDOMAIN"
            );

        var environmentClientId =
            ReadEnvironmentValue(
                "ZENDESK_CLIENT_ID"
            );

        var environmentClientSecret =
            ReadEnvironmentValue(
                "ZENDESK_CLIENT_SECRET"
            );

        if (environmentSubdomain is not null
            && environmentClientId is not null
            && environmentClientSecret is not null)
        {
            return new ResolvedZendeskConnection(
                environmentSubdomain,
                environmentClientId,
                environmentClientSecret
            );
        }

        var configuration =
            await _configurationStore.LoadAsync(
                cancellationToken
            );

        var active =
            GetRequiredActiveProfile(
                configuration
            );

        if (string.IsNullOrWhiteSpace(
                active.Profile.ZendeskConnection))
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' does not reference " +
                "a Zendesk connection. Configure one with " +
                $"SupportToolkit configure profile {active.Name}, or " +
                "provide all ZENDESK_* environment variables."
            );
        }

        var connectionName =
            SupportToolkitConfigurationNames
                .NormalizeConnectionName(
                    active.Profile.ZendeskConnection
                );

        var connection =
            FindZendeskConnection(
                configuration,
                connectionName
            );

        if (connection is null)
        {
            throw new InvalidOperationException(
                $"Active profile '{active.Name}' references Zendesk " +
                $"connection '{connectionName}', but that connection " +
                "does not exist."
            );
        }

        var subdomain =
            environmentSubdomain
            ?? RequireConfiguredValue(
                connection.Subdomain,
                $"Zendesk connection '{connectionName}' subdomain"
            );

        var clientId =
            environmentClientId
            ?? RequireConfiguredValue(
                connection.ClientId,
                $"Zendesk connection '{connectionName}' client ID"
            );

        var clientSecret =
            environmentClientSecret
            ?? await _secretStore.GetSecretAsync(
                SupportToolkitSecretKeys
                    .ZendeskClientSecret(
                        connectionName
                    ),
                cancellationToken
            );

        if (string.IsNullOrWhiteSpace(
                clientSecret))
        {
            throw new InvalidOperationException(
                $"Zendesk client secret for connection " +
                $"'{connectionName}' is not configured. Run " +
                $"SupportToolkit configure profile {active.Name} " +
                "or provide ZENDESK_CLIENT_SECRET."
            );
        }

        return new ResolvedZendeskConnection(
            subdomain,
            clientId,
            clientSecret
        );
    }

    private string? ReadEnvironmentValue(
        string variableName)
    {
        var value =
            _environmentReader(
                variableName
            );

        return string.IsNullOrWhiteSpace(
                value)
            ? null
            : value.Trim();
    }

    private static ActiveProfile GetRequiredActiveProfile(
        SupportToolkitConfiguration configuration)
    {
        if (configuration.Profiles.Count == 0)
        {
            throw new InvalidOperationException(
                "SupportToolkit has no configured profiles. " +
                "Run SupportToolkit configure profile <name> first."
            );
        }

        if (string.IsNullOrWhiteSpace(
                configuration.ActiveProfile))
        {
            throw new InvalidOperationException(
                "SupportToolkit does not have an active profile. " +
                "Run SupportToolkit configure use <name>."
            );
        }

        var profileName =
            SupportToolkitConfigurationNames
                .NormalizeProfileName(
                    configuration.ActiveProfile
                );

        var profile =
            configuration.Profiles
                .FirstOrDefault(
                    pair =>
                        string.Equals(
                            pair.Key,
                            profileName,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                .Value;

        if (profile is null)
        {
            throw new InvalidOperationException(
                $"Active profile '{profileName}' does not exist. " +
                "Run SupportToolkit configure status and select a " +
                "configured profile."
            );
        }

        return new ActiveProfile(
            profileName,
            profile
        );
    }

    private static AcronisConnectionConfiguration?
        FindAcronisConnection(
            SupportToolkitConfiguration configuration,
            string connectionName)
    {
        return configuration.Connections.Acronis
            .FirstOrDefault(
                pair =>
                    string.Equals(
                        pair.Key,
                        connectionName,
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .Value;
    }

    private static ZendeskConnectionConfiguration?
        FindZendeskConnection(
            SupportToolkitConfiguration configuration,
            string connectionName)
    {
        return configuration.Connections.Zendesk
            .FirstOrDefault(
                pair =>
                    string.Equals(
                        pair.Key,
                        connectionName,
                        StringComparison.OrdinalIgnoreCase
                    )
            )
            .Value;
    }

    private static string RequireConfiguredValue(
        string value,
        string description)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new InvalidOperationException(
                $"{description} is not configured."
            );
        }

        return value.Trim();
    }

    private sealed record ActiveProfile(
        string Name,
        SupportToolkitProfile Profile
    );
}