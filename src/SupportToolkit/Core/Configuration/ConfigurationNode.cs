using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Base of every object in the saved configuration file.
///
/// Settings this build does not recognise are kept, exactly as written, and
/// written back when the file is saved. Without this, a build that predates
/// a setting would silently delete it the next time it saved, for example on
/// a profile switch, and nothing could bring it back.
///
/// A setting is only dropped on purpose, by being listed as retired in
/// JsonSupportToolkitConfigurationStore.
/// </summary>
public abstract class ConfigurationNode
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnrecognizedSettings
    {
        get;
        set;
    }
}
