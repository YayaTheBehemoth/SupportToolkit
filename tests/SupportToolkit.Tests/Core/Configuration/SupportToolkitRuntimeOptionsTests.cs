using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Tests.Core.Configuration;

public class SupportToolkitRuntimeOptionsTests
{
    [Fact]
    public void FromEnvironment_WhenModuleModeIsMissing_DefaultsToFixture()
    {
        var options =
            SupportToolkitRuntimeOptions.FromEnvironment(
                "backup-aggregator",
                _ => null
            );

        Assert.Equal(
            SupportToolkitMode.Fixture,
            options.Mode
        );
    }

    [Fact]
    public void FromEnvironment_WhenModuleProductionSpecified_UsesProduction()
    {
        var options =
            SupportToolkitRuntimeOptions.FromEnvironment(
                "backup-aggregator",
                variable =>
                    variable
                    == "SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE"
                        ? "production"
                        : null
            );

        Assert.Equal(
            SupportToolkitMode.Production,
            options.Mode
        );
    }

    [Fact]
    public void FromEnvironment_ResolvesModulesIndependently()
    {
        var values =
            new Dictionary<string, string?>
            {
                ["SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE"] =
                    "fixture",

                ["SUPPORTTOOLKIT_EXAMPLE_MODULE_MODE"] =
                    "production"
            };

        string? ReadEnvironment(
            string variable)
        {
            return values.TryGetValue(
                variable,
                out var value
            )
                ? value
                : null;
        }

        var backupAggregator =
            SupportToolkitRuntimeOptions.FromEnvironment(
                "backup-aggregator",
                ReadEnvironment
            );

        var exampleModule =
            SupportToolkitRuntimeOptions.FromEnvironment(
                "example-module",
                ReadEnvironment
            );

        Assert.Equal(
            SupportToolkitMode.Fixture,
            backupAggregator.Mode
        );

        Assert.Equal(
            SupportToolkitMode.Production,
            exampleModule.Mode
        );
    }

    [Fact]
    public void FromEnvironment_DoesNotUseLegacyGlobalMode()
    {
        var options =
            SupportToolkitRuntimeOptions.FromEnvironment(
                "backup-aggregator",
                variable =>
                    variable == "SUPPORTTOOLKIT_MODE"
                        ? "production"
                        : null
            );

        Assert.Equal(
            SupportToolkitMode.Fixture,
            options.Mode
        );
    }

    [Fact]
    public void FromEnvironment_WhenModuleModeIsInvalid_Throws()
    {
        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    SupportToolkitRuntimeOptions
                        .FromEnvironment(
                            "example-module",
                            variable =>
                                variable
                                == "SUPPORTTOOLKIT_EXAMPLE_MODULE_MODE"
                                    ? "banana"
                                    : null
                        )
            );

        Assert.Contains(
            "SUPPORTTOOLKIT_EXAMPLE_MODULE_MODE",
            exception.Message
        );

        Assert.Contains(
            "fixture",
            exception.Message
        );

        Assert.Contains(
            "production",
            exception.Message
        );
    }

}