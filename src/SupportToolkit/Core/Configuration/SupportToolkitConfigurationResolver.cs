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
public sealed partial class SupportToolkitConfigurationResolver
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