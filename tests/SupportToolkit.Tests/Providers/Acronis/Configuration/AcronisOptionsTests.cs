using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisOptionsTests
{
    [Fact]
    public void FromEnvironment_LoadsRequiredConfiguration()
    {
        var values = new Dictionary<string, string>
        {
            ["ACRONIS_DATACENTER_URL"] =
                "https://fixture.acronis.invalid",

            ["ACRONIS_CLIENT_ID"] =
                "fixture-client",

            ["ACRONIS_CLIENT_SECRET"] =
                "fixture-secret"
        };

        var options = AcronisOptions.FromEnvironment(
            variable =>
                values.TryGetValue(variable, out var value)
                    ? value
                    : null
        );

        Assert.Equal(
            "https://fixture.acronis.invalid",
            options.DatacenterUrl
        );

        Assert.Equal(
            "fixture-client",
            options.ClientId
        );

        Assert.Equal(
            "fixture-secret",
            options.ClientSecret
        );
    }

    [Fact]
    public void FromEnvironment_WhenSecretIsMissing_Throws()
    {
        var values = new Dictionary<string, string>
        {
            ["ACRONIS_DATACENTER_URL"] =
                "https://fixture.acronis.invalid",

            ["ACRONIS_CLIENT_ID"] =
                "fixture-client"
        };

        var exception = Assert.Throws<
            InvalidOperationException>(
            () =>
                AcronisOptions.FromEnvironment(
                    variable =>
                        values.TryGetValue(
                            variable,
                            out var value
                        )
                            ? value
                            : null
                )
        );

        Assert.Contains(
            "ACRONIS_CLIENT_SECRET",
            exception.Message
        );
    }
    [Fact]
    public void FromEnvironment_WhenDatacenterUrlIsNotHttps_Throws()
    {
        var values = new Dictionary<string, string>
        {
            ["ACRONIS_DATACENTER_URL"] =
                "http://fixture.acronis.invalid",

            ["ACRONIS_CLIENT_ID"] =
                "fixture-client",

            ["ACRONIS_CLIENT_SECRET"] =
                "fixture-secret"
        };

        var exception = Assert.Throws<
            InvalidOperationException>(
            () =>
                AcronisOptions.FromEnvironment(
                    variable =>
                        values.TryGetValue(
                            variable,
                            out var value
                        )
                            ? value
                            : null
                )
        );

        Assert.Contains(
            "HTTPS",
            exception.Message
        );
    }

    [Fact]
    public void FromEnvironment_WhenDatacenterUrlIsInvalid_Throws()
    {
        var values = new Dictionary<string, string>
        {
            ["ACRONIS_DATACENTER_URL"] =
                "definitely-not-a-url",

            ["ACRONIS_CLIENT_ID"] =
                "fixture-client",

            ["ACRONIS_CLIENT_SECRET"] =
                "fixture-secret"
        };

        var exception = Assert.Throws<
            InvalidOperationException>(
            () =>
                AcronisOptions.FromEnvironment(
                    variable =>
                        values.TryGetValue(
                            variable,
                            out var value
                        )
                            ? value
                            : null
                )
        );

        Assert.Contains(
            "HTTPS",
            exception.Message
        );
    }
}
