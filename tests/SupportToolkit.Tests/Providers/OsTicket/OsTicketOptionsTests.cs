using SupportToolkit.Providers.OsTicket.Transport;

namespace SupportToolkit.Tests.Providers.OsTicket;

public class OsTicketOptionsTests
{
    private const string Key = "EXAMPLE0API0KEY0000000000000000A";

    [Theory]
    [InlineData("https://helpdesk.example.com", "https://helpdesk.example.com/")]
    [InlineData("https://www.example.com/support", "https://www.example.com/support/")]
    [InlineData("https://www.example.com/support/", "https://www.example.com/support/")]
    [InlineData("http://127.0.0.1:8080/", "http://127.0.0.1:8080/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void AnHttpsUrl_OrPlainHttpOnLoopback_IsTheHelpdeskRoot(
        string configured,
        string expected)
    {
        Assert.Equal(
            expected,
            OsTicketOptions.Create(configured, Key).BaseUri.ToString());
    }

    [Theory]
    [InlineData("http://helpdesk.example.com/")]
    [InlineData("ftp://helpdesk.example.com/")]
    [InlineData("helpdesk.example.com")]
    [InlineData("https://user:pass@helpdesk.example.com/")]
    [InlineData("https://helpdesk.example.com/?x=1")]
    [InlineData("https://helpdesk.example.com/#top")]
    [InlineData("")]
    public void AnythingElse_IsRefused_BecauseTheKeyTravelsInAHeader(
        string configured)
    {
        Assert.Throws<InvalidOperationException>(
            () => OsTicketOptions.Create(configured, Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("KEY WITH SPACE")]
    [InlineData("KEY\r\nX-Other: 1")]
    public void AnUnusableKey_IsRefused_WithoutRepeatingIt(string key)
    {
        var error =
            Assert.Throws<InvalidOperationException>(
                () => OsTicketOptions.Create("https://helpdesk.example.com/", key));

        if (key.Length > 0)
        {
            Assert.DoesNotContain(key, error.Message);
        }
    }
}
