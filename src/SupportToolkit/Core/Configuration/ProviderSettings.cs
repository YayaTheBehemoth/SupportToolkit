using System.Text.Json;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Reads and writes a provider's own connection settings, kept under
/// connections.&lt;section&gt; in the configuration file.
///
/// Core does not know their shape: they are among the connection
/// settings this build keeps as written (ConfigurationNode), and the
/// provider that owns the section turns them into its own types. Section
/// names match case-insensitively; connection names likewise.
/// </summary>
public static class ProviderSettings
{
    public static IReadOnlyDictionary<string, TConnection> ReadConnections<TConnection>(
        SupportToolkitConfiguration configuration,
        string section)
        where TConnection : ConfigurationNode
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        var element = Find(configuration, section)?.Value;

        if (element is null
            || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new Dictionary<string, TConnection>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, TConnection?>? connections;

        try
        {
            connections =
                element.Value.Deserialize<Dictionary<string, TConnection?>>(
                    JsonSupportToolkitConfigurationStore.SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"The configuration's connections.{section} settings could " +
                "not be read. Check that each connection is an object of " +
                "settings.",
                exception);
        }

        var result =
            new Dictionary<string, TConnection>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, connection) in connections ?? [])
        {
            if (connection is not null)
            {
                result[name] = connection;
            }
        }

        return result;
    }

    /// <summary>
    /// Replaces the section with these connections. The configuration is
    /// saved the usual way afterwards.
    /// </summary>
    public static void WriteConnections<TConnection>(
        SupportToolkitConfiguration configuration,
        string section,
        IReadOnlyDictionary<string, TConnection> connections)
        where TConnection : ConfigurationNode
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        ArgumentNullException.ThrowIfNull(connections);

        var settings =
            configuration.Connections.UnrecognizedSettings ??= new();

        var existing = Find(configuration, section);

        if (existing is not null)
        {
            settings.Remove(existing.Value.Key);
        }

        settings[existing?.Key ?? section] =
            JsonSerializer.SerializeToElement(
                connections,
                JsonSupportToolkitConfigurationStore.SerializerOptions);
    }

    private static KeyValuePair<string, JsonElement>? Find(
        SupportToolkitConfiguration configuration,
        string section)
    {
        foreach (var pair in configuration.Connections.UnrecognizedSettings ?? [])
        {
            if (string.Equals(pair.Key, section, StringComparison.OrdinalIgnoreCase))
            {
                return pair;
            }
        }

        return null;
    }
}
