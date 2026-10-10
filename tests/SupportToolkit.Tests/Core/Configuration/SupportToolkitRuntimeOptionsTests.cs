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

                ["SUPPORTTOOLKIT_TICKETING_MODE"] =
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

        var ticketing =
            SupportToolkitRuntimeOptions.FromEnvironment(
                "ticketing",
                ReadEnvironment
            );

        Assert.Equal(
            SupportToolkitMode.Fixture,
            backupAggregator.Mode
        );

        Assert.Equal(
            SupportToolkitMode.Production,
            ticketing.Mode
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
                            "ticketing",
                            variable =>
                                variable
                                == "SUPPORTTOOLKIT_TICKETING_MODE"
                                    ? "banana"
                                    : null
                        )
            );

        Assert.Contains(
            "SUPPORTTOOLKIT_TICKETING_MODE",
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

    [Fact]
    public void GetEnvironmentVariableName_NormalizesModuleCommand()
    {
        var variableName =
            SupportToolkitRuntimeOptions
                .GetEnvironmentVariableName(
                    "backup-aggregator"
                );

        Assert.Equal(
            "SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE",
            variableName
        );
    }
}