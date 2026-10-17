using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Tests.Providers.Acronis;

/// <summary>
/// Shared synthetic HTTP setup for Acronis device-inventory provider tests.
/// Authentication is simulated locally; no real requests are sent.
/// </summary>
internal static class AcronisDeviceTestSupport
{
    public static AcronisDeviceInventoryProvider CreateProvider(HttpClient client)
    {
        return new AcronisDeviceInventoryProvider(
            new AcronisApiClient(
                client,
                new AcronisOptions
                {
                    DatacenterUrl = "https://fixture.acronis.invalid",
                    ClientId = "fixture-client",
                    ClientSecret = "fixture-secret"
                }));
    }

    public static HttpClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);

        return new HttpClient(
            new StubHandler(request =>
                request.RequestUri?.AbsolutePath == "/api/2/idp/token"
                    ? TokenResponse(request.Headers.Authorization)
                    : respond(request)));
    }

    public static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpResponseMessage TokenResponse(
        AuthenticationHeaderValue? authorization)
    {
        var accessToken =
            string.Equals(
                authorization?.Scheme,
                "Basic",
                StringComparison.OrdinalIgnoreCase)
                ? "base-token"
                : "scoped-token";

        return JsonResponse(
            $$"""
            {
              "access_token": "{{accessToken}}",
              "token_type": "bearer",
              "expires_on": 4102444800
            }
            """);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_respond(request));
        }
    }
}
