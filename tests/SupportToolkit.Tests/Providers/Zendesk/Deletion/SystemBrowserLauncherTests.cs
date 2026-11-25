using System.ComponentModel;
using System.Diagnostics;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;

namespace SupportToolkit.Tests.Providers.Zendesk.Deletion;

/// <summary>
/// The launcher never starts a real process here: the start is replaced.
/// </summary>
public class SystemBrowserLauncherTests
{
    private static readonly Uri SignIn =
        new("https://example.zendesk.com/oauth/authorizations/new?client_id=example");

    [Fact]
    public void WhenNoBrowserCanBeOpened_TheLinkIsHandedOn_AndNothingThrows()
    {
        Uri? handedOn = null;

        var launcher =
            new SystemBrowserLauncher(
                link => handedOn = link,
                _ => throw new Win32Exception("no browser"));

        launcher.Open(SignIn);

        Assert.Equal(SignIn, handedOn);
    }

    [Fact]
    public void WithoutSomewhereToShowTheLink_AFailedLaunch_IsASignInError()
    {
        var launcher =
            new SystemBrowserLauncher(
                start: _ => throw new InvalidOperationException("no shell"));

        var error =
            Assert.Throws<ZendeskAuthenticationException>(
                () => launcher.Open(SignIn));

        Assert.Contains("No browser could be opened", error.Message);
    }

    [Fact]
    public void AnOpenedBrowser_HandsNothingOn()
    {
        ProcessStartInfo? started = null;
        var handedOn = false;

        var launcher =
            new SystemBrowserLauncher(
                _ => handedOn = true,
                info => started = info);

        launcher.Open(SignIn);

        Assert.Equal(SignIn.AbsoluteUri, started!.FileName);
        Assert.True(started.UseShellExecute);
        Assert.False(handedOn);
    }

    [Fact]
    public void OnlyAnHttpsPage_IsEverOpenedOrHandedOn()
    {
        var handedOn = false;

        var launcher =
            new SystemBrowserLauncher(
                _ => handedOn = true,
                _ => throw new Win32Exception("no browser"));

        Assert.Throws<InvalidOperationException>(
            () => launcher.Open(new Uri("http://example.zendesk.com/")));

        Assert.False(handedOn);
    }
}
