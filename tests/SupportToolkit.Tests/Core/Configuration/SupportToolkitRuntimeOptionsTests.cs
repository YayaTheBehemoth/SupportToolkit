using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Tests.Core.Configuration;

public class SupportToolkitRuntimeOptionsTests
{
    [Fact]
    public void FromEnvironment_WhenModeIsMissing_DefaultsToFixture()
    {
        var options =
            SupportToolkitRuntimeOptions.FromEnvironment(
                _ => null
            );

        Assert.Equal(
            SupportToolkitMode.Fixture,
            options.Mode
        );
    }

    [Fact]
    public void FromEnvironment_WhenProductionSpecified_UsesProduction()
    {
        var options =
            SupportToolkitRuntimeOptions.FromEnvironment(
                variable =>
                    variable == "SUPPORTTOOLKIT_MODE"
                        ? "production"
                        : null
            );

        Assert.Equal(
            SupportToolkitMode.Production,
            options.Mode
        );
    }

    [Fact]
    public void FromEnvironment_WhenModeIsInvalid_Throws()
    {
        var exception = Assert.Throws<
            InvalidOperationException>(
            () =>
                SupportToolkitRuntimeOptions
                    .FromEnvironment(
                        _ => "banana"
                    )
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