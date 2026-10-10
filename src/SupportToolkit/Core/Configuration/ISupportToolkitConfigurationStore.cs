namespace SupportToolkit.Core.Configuration;

/// <summary>
/// Abstraction over persistent non-secret SupportToolkit configuration.
/// </summary>
public interface ISupportToolkitConfigurationStore
{
    string ConfigurationPath { get; }

    Task<SupportToolkitConfiguration> LoadAsync(
        CancellationToken cancellationToken = default
    );

    Task SaveAsync(
        SupportToolkitConfiguration configuration,
        CancellationToken cancellationToken = default
    );
}