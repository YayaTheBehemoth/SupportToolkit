using SupportToolkit.Providers.BusinessCentral.Transport;
using static SupportToolkit.Tests.Providers.BusinessCentral.BusinessCentralTestSupport;

namespace SupportToolkit.Tests.Providers.BusinessCentral;

public class BusinessCentralOptionsTests
{
    [Fact]
    public void Options_DeriveTenantPinnedAuthorityAndApiBase()
    {
        var options = Options();

        Assert.Equal(
            $"https://login.microsoftonline.com/{TenantId}/",
            options.AuthorityUri.ToString());

        Assert.Equal(
            "https://api.businesscentral.dynamics.com/v2.0/" +
            $"{TenantId}/Sandbox/api/example/monitoring/v1.0/",
            options.ApiBaseUri.ToString());

        Assert.Equal(
            $"companies({CompanyId})",
            options.CompanyPath);
    }

    [Fact]
    public void Options_NormalizeGuidCase()
    {
        var options =
            BusinessCentralOptions.Create(
                TenantId.ToUpperInvariant(),
                ClientId,
                "Sandbox",
                CompanyId,
                "example",
                "monitoring",
                "v1.0");

        Assert.Equal(TenantId, options.TenantId);
    }

    [Theory]
    [InlineData("tenant", ClientId, "Sandbox", CompanyId, "p", "g", "v1.0")]
    [InlineData(TenantId, "common", "Sandbox", CompanyId, "p", "g", "v1.0")]
    [InlineData(TenantId, ClientId, "Sandbox", "not-a-guid", "p", "g", "v1.0")]
    [InlineData(TenantId, ClientId, "", CompanyId, "p", "g", "v1.0")]
    [InlineData(TenantId, ClientId, "../Production", CompanyId, "p", "g", "v1.0")]
    [InlineData(TenantId, ClientId, "Sandbox", CompanyId, "p/x", "g", "v1.0")]
    [InlineData(TenantId, ClientId, "Sandbox", CompanyId, "p", "g?x=1", "v1.0")]
    [InlineData(TenantId, ClientId, "Sandbox", CompanyId, "p", "g", "v1..0")]
    [InlineData(TenantId, ClientId, "evil.example@host", CompanyId, "p", "g", "v1.0")]
    public void InvalidOrUnsafeValues_AreRejected(
        string tenant,
        string client,
        string environment,
        string company,
        string publisher,
        string group,
        string version)
    {
        Assert.Throws<InvalidOperationException>(
            () => BusinessCentralOptions.Create(
                tenant,
                client,
                environment,
                company,
                publisher,
                group,
                version));
    }

    [Fact]
    public void PinnedHostsAndScope_AreCodeConstants()
    {
        Assert.Equal(
            "api.businesscentral.dynamics.com",
            BusinessCentralOptions.ApiHost);

        Assert.Equal(
            "login.microsoftonline.com",
            BusinessCentralOptions.AuthorityHost);

        Assert.StartsWith(
            "https://api.businesscentral.dynamics.com/",
            BusinessCentralOptions.DelegatedScope);
    }
}
