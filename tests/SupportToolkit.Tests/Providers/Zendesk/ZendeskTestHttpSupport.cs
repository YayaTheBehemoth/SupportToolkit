using System.Net;
using System.Text;
using SupportToolkit.Providers.Zendesk.Tickets;

namespace SupportToolkit.Tests.Providers.Zendesk;

internal static class ZendeskTestHttpSupport
{
    // Fictional Zendesk user IDs.
    public const long FixtureSubmitterId = 900000000001L;

    public const long FixtureRequesterId = 900000000002L;

    public static ZendeskTicketIdentity Identity() =>
        new(FixtureSubmitterId, FixtureRequesterId);

    public static HttpClient CreateClient(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        return new HttpClient(new StubHandler(respond));
    }

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _respond(request);
        }
    }
}
