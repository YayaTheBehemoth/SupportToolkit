using SupportToolkit.Providers.Zendesk.Transport;

namespace SupportToolkit.Tests.Providers.Zendesk;

public class ZendeskOptionsTests
{
    [Fact]
    public void FromEnvironment_LoadsRequiredConfiguration()
    {
        var values =
            new Dictionary<string, string?>
            {
                ["ZENDESK_SUBDOMAIN"] =
                    "supporttoolkit-test",

                ["ZENDESK_CLIENT_ID"] =
                    "supportToolkit",

                ["ZENDESK_CLIENT_SECRET"] =
                    "fixture-secret"
            };

        var options =
            ZendeskOptions.FromEnvironment(
                name =>
                    values.TryGetValue(
                        name,
                        out var value
                    )
                        ? value
                        : null
            );

        Assert.Equal(
            "supporttoolkit-test",
            options.Subdomain
        );

        Assert.Equal(
            "supportToolkit",
            options.ClientId
        );

        Assert.Equal(
            "fixture-secret",
            options.ClientSecret
        );

        Assert.Equal(
            "https://supporttoolkit-test.zendesk.com/",
            options.BaseUri.ToString()
        );
    }

    [Fact]
    public void FromEnvironment_RejectsInvalidSubdomain()
    {
        var values =
            new Dictionary<string, string?>
            {
                ["ZENDESK_SUBDOMAIN"] =
                    "https://evil.example",

                ["ZENDESK_CLIENT_ID"] =
                    "supportToolkit",

                ["ZENDESK_CLIENT_SECRET"] =
                    "fixture-secret"
            };

        Assert.Throws<InvalidOperationException>(
            () =>
                ZendeskOptions.FromEnvironment(
                    name =>
                        values.TryGetValue(
                            name,
                            out var value
                        )
                            ? value
                            : null
                )
        );
    }
}