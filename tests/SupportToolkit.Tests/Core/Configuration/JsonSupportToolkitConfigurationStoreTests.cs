using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Tests.Core.Configuration;

public class JsonSupportToolkitConfigurationStoreTests
{
    [Fact]
    public void CreateDefault_WhenRepositoryRootExists_UsesRootLocalConfig()
    {
        var temporaryRoot =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            File.WriteAllText(
                Path.Combine(
                    temporaryRoot.FullName,
                    "SupportToolkit.slnx"
                ),
                string.Empty
            );

            var nestedDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        temporaryRoot.FullName,
                        "src",
                        "SupportToolkit"
                    )
                );

            var store =
                JsonSupportToolkitConfigurationStore
                    .CreateDefault(
                        _ => null,
                        nestedDirectory.FullName
                    );

            Assert.Equal(
                Path.Combine(
                    temporaryRoot.FullName,
                    "local.config.json"
                ),
                store.ConfigurationPath
            );
        }
        finally
        {
            temporaryRoot.Delete(
                recursive:
                    true
            );
        }
    }

    [Fact]
    public void CreateDefault_WhenExplicitPathExists_UsesOverride()
    {
        var temporaryRoot =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            var configuredPath =
                Path.Combine(
                    temporaryRoot.FullName,
                    "custom.config.json"
                );

            var store =
                JsonSupportToolkitConfigurationStore
                    .CreateDefault(
                        variable =>
                            variable
                            == JsonSupportToolkitConfigurationStore
                                .ConfigurationPathEnvironmentVariable
                                ? configuredPath
                                : null,
                        temporaryRoot.FullName
                    );

            Assert.Equal(
                configuredPath,
                store.ConfigurationPath
            );
        }
        finally
        {
            temporaryRoot.Delete(
                recursive:
                    true
            );
        }
    }

    [Fact]
    public void CreateDefault_WhenRelativeOverrideExists_ResolvesFromWorkingDirectory()
    {
        var temporaryRoot =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            var store =
                JsonSupportToolkitConfigurationStore
                    .CreateDefault(
                        variable =>
                            variable
                            == JsonSupportToolkitConfigurationStore
                                .ConfigurationPathEnvironmentVariable
                                ? "configuration\\custom.json"
                                : null,
                        temporaryRoot.FullName
                    );

            Assert.Equal(
                Path.Combine(
                    temporaryRoot.FullName,
                    "configuration",
                    "custom.json"
                ),
                store.ConfigurationPath
            );
        }
        finally
        {
            temporaryRoot.Delete(
                recursive:
                    true
            );
        }
    }

    [Fact]
    public void CreateDefault_WhenRepositoryRootCannotBeFound_Throws()
    {
        var temporaryRoot =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            var exception =
                Assert.Throws<InvalidOperationException>(
                    () =>
                        JsonSupportToolkitConfigurationStore
                            .CreateDefault(
                                _ => null,
                                temporaryRoot.FullName
                            )
                );

            Assert.Contains(
                "SUPPORTTOOLKIT_CONFIG",
                exception.Message
            );

            Assert.Contains(
                "repository root",
                exception.Message,
                StringComparison.OrdinalIgnoreCase
            );
        }
        finally
        {
            temporaryRoot.Delete(
                recursive:
                    true
            );
        }
    }
}