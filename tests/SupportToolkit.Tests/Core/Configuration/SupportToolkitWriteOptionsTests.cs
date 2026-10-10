using SupportToolkit.Core.Configuration;

namespace SupportToolkit.Tests.Core.Configuration;

public class SupportToolkitWriteOptionsTests
{
    [Fact]
    public void FromEnvironment_WhenMissing_DisablesWrites()
    {
        var options =
            SupportToolkitWriteOptions
                .FromEnvironment(
                    _ => null
                );

        Assert.False(
            options.AllowWrites
        );
    }

    [Fact]
    public void FromEnvironment_WhenTrue_EnablesWrites()
    {
        var options =
            SupportToolkitWriteOptions
                .FromEnvironment(
                    variable =>
                        variable
                        == "SUPPORTTOOLKIT_ALLOW_WRITES"
                            ? "true"
                            : null
                );

        Assert.True(
            options.AllowWrites
        );
    }

    [Fact]
    public void FromEnvironment_WhenFalse_DisablesWrites()
    {
        var options =
            SupportToolkitWriteOptions
                .FromEnvironment(
                    variable =>
                        variable
                        == "SUPPORTTOOLKIT_ALLOW_WRITES"
                            ? "false"
                            : null
                );

        Assert.False(
            options.AllowWrites
        );
    }

    [Fact]
    public void FromEnvironment_WhenInvalid_Throws()
    {
        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    SupportToolkitWriteOptions
                        .FromEnvironment(
                            _ => "banana"
                        )
            );

        Assert.Contains(
            "SUPPORTTOOLKIT_ALLOW_WRITES",
            exception.Message
        );
    }
}