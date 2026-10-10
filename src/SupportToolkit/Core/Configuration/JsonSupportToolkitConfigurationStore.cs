using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Persists non-secret SupportToolkit configuration as JSON in the current
/// user's local application-data directory.
/// </summary>
public sealed class JsonSupportToolkitConfigurationStore
    : ISupportToolkitConfigurationStore
{
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
        CreateDefault()
    {
        var applicationDataDirectory =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );

        if (string.IsNullOrWhiteSpace(
                applicationDataDirectory))
        {
            throw new InvalidOperationException(
                "Windows local application-data directory " +
                "could not be resolved."
            );
        }

        var configurationPath =
            Path.Combine(
                applicationDataDirectory,
                "SupportToolkit",
                "config.json"
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

            configuration.Profiles ??=
                new Dictionary<
                    string,
                    SupportToolkitProfile>();

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

    private static JsonSerializerOptions
        CreateSerializerOptions()
    {
        var options =
            new JsonSerializerOptions
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,

                WriteIndented =
                    true
            };

        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase
            )
        );

        return options;
    }
}