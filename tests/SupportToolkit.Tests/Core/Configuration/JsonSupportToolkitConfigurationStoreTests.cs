
using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Tests.Core.Configuration;

public class JsonSupportToolkitConfigurationStoreTests
{
    [Fact]
    public void DebugBuild_InRepository_UsesRootLocalConfig()
    {
        var root =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            CreateSolutionMarker(
                root.FullName
            );

            var applicationDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        root.FullName,
                        "src",
                        "SupportToolkit",
                        "bin",
                        "Debug",
                        "net10.0"
                    )
                );

            var store =
                JsonSupportToolkitConfigurationStore
                    .CreateDefault(
                        _ => null,
                        root.FullName,
                        applicationDirectory.FullName,
                        isDevelopmentBuild: true
                    );

            Assert.Equal(
                Path.Combine(
                    root.FullName,
                    "local.config.json"
                ),
                store.ConfigurationPath
            );
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void DebugBuild_OutsideRepository_UsesLocalAppData()
    {
        var root =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            var store =
                JsonSupportToolkitConfigurationStore
                    .CreateDefault(
                        _ => null,
                        root.FullName,
                        root.FullName,
                        isDevelopmentBuild: true
                    );

            Assert.Equal(
                ExpectedLocalAppDataPath(),
                store.ConfigurationPath
            );
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReleaseBuild_InsideRepository_UsesLocalAppData()
    {
        var root =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            CreateSolutionMarker(
                root.FullName
            );

            var publishDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        root.FullName,
                        "artifacts",
                        "publish",
                        "win-x64"
                    )
                );

            var store =
                JsonSupportToolkitConfigurationStore
                    .CreateDefault(
                        _ => null,
                        root.FullName,
                        publishDirectory.FullName,
                        isDevelopmentBuild: false
                    );

            Assert.Equal(
                ExpectedLocalAppDataPath(),
                store.ConfigurationPath
            );
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReleaseBuild_OutsideRepository_UsesLocalAppData()
    {
        var root =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            var store =
                JsonSupportToolkitConfigurationStore
                    .CreateDefault(
                        _ => null,
                        root.FullName,
                        root.FullName,
                        isDevelopmentBuild: false
                    );

            Assert.Equal(
                ExpectedLocalAppDataPath(),
                store.ConfigurationPath
            );
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void DebugBuild_ExternalWorkingDirectory_StillUsesRepository()
    {
        var root =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            var repository =
                Directory.CreateDirectory(
                    Path.Combine(
                        root.FullName,
                        "repository"
                    )
                );

            CreateSolutionMarker(
                repository.FullName
            );

            var applicationDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        repository.FullName,
                        "src",
                        "SupportToolkit",
                        "bin"
                    )
                );

            var externalDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        root.FullName,
                        "external"
                    )
                );

            var store =
                JsonSupportToolkitConfigurationStore
                    .CreateDefault(
                        _ => null,
                        externalDirectory.FullName,
                        applicationDirectory.FullName,
                        isDevelopmentBuild: true
                    );

            Assert.Equal(
                Path.Combine(
                    repository.FullName,
                    "local.config.json"
                ),
                store.ConfigurationPath
            );
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReleaseBuild_RepositoryWorkingDirectory_UsesLocalAppData()
    {
        var root =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            var repository =
                Directory.CreateDirectory(
                    Path.Combine(
                        root.FullName,
                        "repository"
                    )
                );

            CreateSolutionMarker(
                repository.FullName
            );

            var portableDirectory =
                Directory.CreateDirectory(
                    Path.Combine(
                        root.FullName,
                        "portable"
                    )
                );

            var store =
                JsonSupportToolkitConfigurationStore
                    .CreateDefault(
                        _ => null,
                        repository.FullName,
                        portableDirectory.FullName,
                        isDevelopmentBuild: false
                    );

            Assert.Equal(
                ExpectedLocalAppDataPath(),
                store.ConfigurationPath
            );
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitAbsoluteOverride_AlwaysTakesPrecedence(
        bool isDevelopmentBuild)
    {
        var root =
            Directory.CreateTempSubdirectory(
                "supporttoolkit-config-test-"
            );

        try
        {
            var configuredPath =
                Path.Combine(
                    root.FullName,
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
                        root.FullName,
                        root.FullName,
                        isDevelopmentBuild
                    );

            Assert.Equal(
                configuredPath,
                store.ConfigurationPath
            );
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitRelativeOverride_UsesWorkingDirectory(
        bool isDevelopmentBuild)
    {
        var root =
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
                                ? Path.Combine(
                                    "configuration",
                                    "custom.json"
                                )
                                : null,
                        root.FullName,
                        root.FullName,
                        isDevelopmentBuild
                    );

            Assert.Equal(
                Path.Combine(
                    root.FullName,
                    "configuration",
                    "custom.json"
                ),
                store.ConfigurationPath
            );
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static void CreateSolutionMarker(
        string directory)
    {
        File.WriteAllText(
            Path.Combine(
                directory,
                "SupportToolkit.slnx"
            ),
            string.Empty
        );
    }

    private static string ExpectedLocalAppDataPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            ),
            "SupportToolkit",
            "local.config.json"
        );
    }
}
