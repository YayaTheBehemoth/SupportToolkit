using SupportToolkit.Ui.Web;

namespace SupportToolkit.Tests.Ui.Web;

public class LaunchSecurityTests
{
    private const int Port = 51234;

    private static LaunchSecurity Listening()
    {
        var security = new LaunchSecurity("launch-token", "session-value");

        security.SetListeningPort(Port);

        return security;
    }

    [Fact]
    public void EveryRequestIsRefused_UntilThePortIsKnown()
    {
        var security = new LaunchSecurity("launch-token", "session-value");

        Assert.False(security.IsAllowedHost($"127.0.0.1:{Port}"));
        Assert.False(security.IsAllowedHost($"localhost:{Port}"));
        Assert.False(security.IsAllowedOrigin($"http://127.0.0.1:{Port}"));
    }

    [Theory]
    [InlineData("127.0.0.1:51234")]
    [InlineData("localhost:51234")]
    [InlineData("LOCALHOST:51234")]
    public void TheLoopbackHostsWithThePort_AreAllowed(string host)
    {
        Assert.True(Listening().IsAllowedHost(host));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("127.0.0.1:51235")]
    [InlineData("evil.test")]
    [InlineData("evil.test:51234")]
    [InlineData("localhost.evil.test:51234")]
    [InlineData("127.0.0.1.evil.test:51234")]
    [InlineData("[::1]:51234")]
    [InlineData("0.0.0.0:51234")]
    [InlineData("192.168.1.10:51234")]
    public void AnyOtherHost_IsRefused_WhichStopsDnsRebinding(string? host)
    {
        Assert.False(Listening().IsAllowedHost(host));
    }

    [Theory]
    [InlineData("http://127.0.0.1:51234")]
    [InlineData("http://localhost:51234")]
    public void TheUisOwnOrigin_IsAllowed(string origin)
    {
        Assert.True(Listening().IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("https://127.0.0.1:51234")]
    [InlineData("http://127.0.0.1:51235")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://evil.test")]
    [InlineData("http://localhost.evil.test:51234")]
    [InlineData("http://127.0.0.1:51234/")]
    public void AnyOtherOrigin_IsRefused(string? origin)
    {
        Assert.False(Listening().IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("POST", false, true)]
    [InlineData("PUT", false, true)]
    [InlineData("PATCH", false, true)]
    [InlineData("DELETE", false, true)]
    [InlineData("GET", true, true)]
    [InlineData("GET", false, false)]
    [InlineData("HEAD", false, false)]
    [InlineData("OPTIONS", false, false)]
    public void StateChangingRequestsAndWebSocketsNeedAnOrigin(
        string method,
        bool webSocket,
        bool expected)
    {
        Assert.Equal(
            expected,
            LaunchSecurity.RequiresOriginCheck(method, webSocket));
    }

    [Fact]
    public void TheLaunchToken_AndTheSessionValue_AreDifferentSecrets()
    {
        var security = Listening();

        Assert.True(security.IsValidLaunchToken("launch-token"));
        Assert.False(security.IsValidLaunchToken("session-value"));

        Assert.True(security.IsValidSession("session-value"));
        Assert.False(security.IsValidSession("launch-token"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("launch-toke")]
    [InlineData("launch-token ")]
    [InlineData("LAUNCH-TOKEN")]
    public void ATokenMustMatchExactly(string? candidate)
    {
        var security = Listening();

        Assert.False(security.IsValidLaunchToken(candidate));
        Assert.False(security.IsValidSession(candidate));
    }

    [Fact]
    public void EachLaunch_GetsItsOwnLongRandomSecrets()
    {
        var first = new LaunchSecurity();
        var second = new LaunchSecurity();

        Assert.NotEqual(first.LaunchToken, second.LaunchToken);
        Assert.NotEqual(first.SessionValue, second.SessionValue);
        Assert.NotEqual(first.LaunchToken, first.SessionValue);

        Assert.True(first.LaunchToken.Length >= 40);
        Assert.True(first.SessionValue.Length >= 40);

        // Safe in a URL and a cookie without any escaping.
        Assert.All(
            first.LaunchToken + first.SessionValue,
            character => Assert.True(
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_'));
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("x", "")]
    [InlineData(" ", "x")]
    public void BlankSecrets_AreRejected(string token, string session)
    {
        Assert.Throws<ArgumentException>(
            () => new LaunchSecurity(token, session));
    }
}
