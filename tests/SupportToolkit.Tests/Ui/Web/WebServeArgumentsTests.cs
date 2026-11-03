using SupportToolkit.Ui.Web;

namespace SupportToolkit.Tests.Ui.Web;

public class WebServeArgumentsTests
{
    [Fact]
    public void NoArguments_PicksAFreePort_AndOpensTheBrowser()
    {
        Assert.True(
            WebServeArguments.TryParse([], out var options, out _));

        Assert.Equal(0, options!.Port);
        Assert.True(options.OpenBrowser);
    }

    [Fact]
    public void NoBrowser_And_Port_AreParsed()
    {
        Assert.True(
            WebServeArguments.TryParse(
                ["--port", "5280", "--no-browser"],
                out var options,
                out _));

        Assert.Equal(5280, options!.Port);
        Assert.False(options.OpenBrowser);
    }

    [Theory]
    [InlineData("--PORT", "8080")]
    [InlineData("--Port", "65535")]
    [InlineData("--port", "1")]
    public void TheOptionsAreCaseInsensitive_AndTheEdgesOfThePortRangeWork(
        string name,
        string value)
    {
        Assert.True(
            WebServeArguments.TryParse([name, value], out _, out _));
    }

    [Theory]
    [InlineData("--port")]
    [InlineData("--port", "abc")]
    [InlineData("--port", "0")]
    [InlineData("--port", "-1")]
    [InlineData("--port", "65536")]
    [InlineData("--port", "80.5")]
    [InlineData("--port", "1", "--port", "2")]
    [InlineData("--no-browser", "--no-browser")]
    [InlineData("--host", "0.0.0.0")]
    [InlineData("--bind", "0.0.0.0")]
    [InlineData("--urls", "http://0.0.0.0:5000")]
    [InlineData("extra")]
    public void Everything_Else_IsRefused_IncludingAnyWayToListenBeyondLoopback(
        params string[] args)
    {
        Assert.False(
            WebServeArguments.TryParse(args, out var options, out var error));

        Assert.Null(options);
        Assert.Contains("Usage:", error);
    }
}
