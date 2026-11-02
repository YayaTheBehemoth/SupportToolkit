using System.Net;
using SupportToolkit.Providers.Zendesk.Deletion.Auth;

namespace SupportToolkit.Tests.Providers.Zendesk.Deletion;

/// <summary>
/// Uses a real loopback socket on this machine. It never reaches Zendesk
/// or any other host.
/// </summary>
public class ZendeskLoopbackReceiverTests
{
    [Fact]
    public async Task Receiver_ReturnsOnlyTheRecognisedParameters_AndAnswersWithAFixedPage()
    {
        using var receiver = ZendeskLoopbackReceiver.Start();

        Assert.StartsWith("http://localhost:", receiver.RedirectUri);

        var waiting =
            receiver.WaitForRedirectAsync(CancellationToken.None);

        using var http = new HttpClient();

        using var response =
            await http.GetAsync(
                receiver.RedirectUri +
                "/?code=example-code&state=example-state&other=ignored" +
                "&error_description=" + Uri.EscapeDataString("<b>x</b>"));

        var body = await response.Content.ReadAsStringAsync();

        var received =
            await waiting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("example-code", received["code"]);
        Assert.Equal("example-state", received["state"]);
        Assert.False(received.ContainsKey("other"));

        // Nothing from the request is echoed back to the browser.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("example-code", body);
        Assert.DoesNotContain("<b>", body);
    }

    [Fact]
    public async Task Receiver_IgnoresStrayRequests_UntilTheRealRedirectArrives()
    {
        using var receiver = ZendeskLoopbackReceiver.Start();

        var waiting =
            receiver.WaitForRedirectAsync(CancellationToken.None);

        using var http = new HttpClient();

        using var stray =
            await http.GetAsync(receiver.RedirectUri + "/favicon.ico");

        Assert.Equal(HttpStatusCode.NotFound, stray.StatusCode);
        Assert.False(waiting.IsCompleted);

        using var real =
            await http.GetAsync(
                receiver.RedirectUri + "/?error=access_denied");

        var received =
            await waiting.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("access_denied", received["error"]);
    }

    [Fact]
    public async Task Receiver_StopsWaiting_WhenCancelled()
    {
        using var receiver = ZendeskLoopbackReceiver.Start();
        using var cancel = new CancellationTokenSource();

        var waiting = receiver.WaitForRedirectAsync(cancel.Token);

        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waiting);
    }

    [Fact]
    public void EachReceiver_GetsItsOwnFreePort()
    {
        using var first = ZendeskLoopbackReceiver.Start();
        using var second = ZendeskLoopbackReceiver.Start();

        Assert.NotEqual(first.RedirectUri, second.RedirectUri);
    }
}
