using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Persists non-secret SupportToolkit configuration as JSON.
///
/// By default, configuration lives in local.config.json at the SupportToolkit
/// repository root. An explicit SUPPORTTOOLKIT_CONFIG environment variable may
/// override that location.
///
/// Authentication secrets are not stored by this class.
/// </summary>
public sealed class JsonSupportToolkitConfigurationStore
    : ISupportToolkitConfigurationStore
{
    public const string ConfigurationPathEnvironmentVariable =
        "SUPPORTTOOLKIT_CONFIG";

    public const string DefaultConfigurationFileName =
        "local.config.json";

    private const string SolutionFileName =
        "SupportToolkit.slnx";

    private readonly JsonSerializerOptions _serializerOptions;

    public string ConfigurationPath { get; }

    public JsonSupportToolkitConfigurationStore(
        string configurationPath)
    {
        if (string.IsNullOrWhiteSpace(
                configurationPath))
        {
            throw new ArgumentException(
                "Configuration path cannot be empty.",
                nameof(configurationPath)
            );
        }

        ConfigurationPath =
            Path.GetFullPath(
                configurationPath
            );

        _serializerOptions =
            CreateSerializerOptions();
    }

    public static JsonSupportToolkitConfigurationStore
        CreateDefault(
            Func<string, string?>? environmentReader = null,
            string? currentDirectory = null)
    {
        environmentReader ??=
            Environment.GetEnvironmentVariable;

        var workingDirectory =
            string.IsNullOrWhiteSpace(
                currentDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(
                    currentDirectory
                );

        var configuredPath =
            environmentReader(
                ConfigurationPathEnvironmentVariable
            );

        if (!string.IsNullOrWhiteSpace(
                configuredPath))
        {
            var trimmedPath =
                configuredPath.Trim();

            var resolvedPath =
                Path.IsPathRooted(
                    trimmedPath)
                    ? trimmedPath
                    : Path.Combine(
                        workingDirectory,
                        trimmedPath
                    );

            return new JsonSupportToolkitConfigurationStore(
                resolvedPath
            );
        }

        var repositoryRoot =
            FindRepositoryRoot(
                workingDirectory
            );

        var configurationPath =
            Path.Combine(
                repositoryRoot,
                DefaultConfigurationFileName
            );

        return new JsonSupportToolkitConfigurationStore(
            configurationPath
        );
    }

    public async Task<SupportToolkitConfiguration> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(
                ConfigurationPath))
        {
            return new SupportToolkitConfiguration();
        }

        try
        {
            await using var stream =
                new FileStream(
                    ConfigurationPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize:
                        4096,
                    useAsync:
                        true
                );

            var configuration =
                await JsonSerializer.DeserializeAsync<
                    SupportToolkitConfiguration>(
                    stream,
                    _serializerOptions,
                    cancellationToken
                );

            if (configuration is null)
            {
                throw new InvalidOperationException(
                    "SupportToolkit configuration file was empty."
                );
            }

            NormalizeConfiguration(
                configuration
            );

            return configuration;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"SupportToolkit configuration file is invalid JSON: " +
                $"{ConfigurationPath}",
                exception
            );
        }
    }

    public async Task SaveAsync(
        SupportToolkitConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            configuration
        );

        NormalizeConfiguration(
            configuration
        );

        var directory =
            Path.GetDirectoryName(
                ConfigurationPath
            );

        if (string.IsNullOrWhiteSpace(
                directory))
        {
            throw new InvalidOperationException(
                "SupportToolkit configuration directory " +
                "could not be resolved."
            );
        }

        Directory.CreateDirectory(
            directory
        );

        var temporaryPath =
            ConfigurationPath
            + ".tmp";

        try
        {
            var json =
                JsonSerializer.Serialize(
                    configuration,
                    _serializerOptions
                );

            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier:
                        false
                ),
                cancellationToken
            );

            File.Move(
                temporaryPath,
                ConfigurationPath,
                overwrite:
                    true
            );
        }
        finally
        {
            if (File.Exists(
                    temporaryPath))
            {
                File.Delete(
                    temporaryPath
                );
            }
        }
    }

    private static void NormalizeConfiguration(
        SupportToolkitConfiguration configuration)
    {
        configuration.Connections ??=
            new SupportToolkitConnections();

        configuration.Connections.Acronis ??=
            new Dictionary<
                string,
                AcronisConnectionConfiguration>();

        configuration.Connections.Zendesk ??=
            new Dictionary<
                string,
                ZendeskConnectionConfiguration>();

        configuration.Profiles ??=
            new Dictionary<
                string,
                SupportToolkitProfile>();

        foreach (var profile in configuration.Profiles.Values)
        {
            profile.Modules ??=
                new Dictionary<
                    string,
                    SupportToolkitMode>();
        }
    }

    private static string FindRepositoryRoot(
        string startingDirectory)
    {
        var directory =
            new DirectoryInfo(
                startingDirectory
            );

        while (directory is not null)
        {
            var solutionPath =
                Path.Combine(
                    directory.FullName,
                    SolutionFileName
                );

            if (File.Exists(
                    solutionPath))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the SupportToolkit repository root. " +
            $"Run SupportToolkit from within the repository tree or set " +
            $"'{ConfigurationPathEnvironmentVariable}' to an explicit " +
            $"configuration file path."
        );
    }

    private static JsonSerializerOptions
        CreateSerializerOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,

                WriteIndented =
                    true,

                DefaultIgnoreCondition =
                    JsonIgnoreCondition.WhenWritingNull
            };

        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase
            )
        );

        return options;
    }
}