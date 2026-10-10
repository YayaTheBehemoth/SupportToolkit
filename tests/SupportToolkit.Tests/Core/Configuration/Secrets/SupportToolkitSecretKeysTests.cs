using SupportToolkit.Core.Secrets;

namespace SupportToolkit.Tests.Core.Secrets;

public class SupportToolkitSecretKeysTests
{
    [Fact]
    public void AcronisClientSecret_IsConnectionScoped()
    {
        var key =
            SupportToolkitSecretKeys
                .AcronisClientSecret(
                    "Work-Production"
                );

        Assert.Equal(
            "acronis:work-production:client-secret",
            key
        );
    }

    [Fact]
    public void ZendeskClientSecret_IsConnectionScoped()
    {
        var key =
            SupportToolkitSecretKeys
                .ZendeskClientSecret(
                    "Personal-Sandbox"
                );

        Assert.Equal(
            "zendesk:personal-sandbox:client-secret",
            key
        );
    }

    [Fact]
    public void ProvidersWithSameConnectionName_DoNotShareSecretKeys()
    {
        var acronis =
            SupportToolkitSecretKeys
                .AcronisClientSecret(
                    "production"
                );

        var zendesk =
            SupportToolkitSecretKeys
                .ZendeskClientSecret(
                    "production"
                );

        Assert.NotEqual(
            acronis,
            zendesk
        );
    }
}