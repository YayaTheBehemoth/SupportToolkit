using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Tests.Providers.Acronis;

/// <summary>
/// Synthetic authentication and HTTP setup shared by provider contract tests.
/// It never sends requests to Acronis. Authentication-specific tests use
/// their own handlers to assert the actual authorization behavior.
/// </summary>
internal static class AcronisProviderTestSupport
{
    public static HttpClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);

        return new HttpClient(new StubHandler(request =>
            request.RequestUri?.AbsolutePath == "/api/2/idp/token"
                ? TokenResponse(request.Headers.Authorization)
                : respond(request)));
    }

    public static AcronisApiClient CreateApiClient(HttpClient client)
    {
        return new AcronisApiClient(
            client,
            new AcronisOptions
            {
                DatacenterUrl = "https://fixture.acronis.invalid",
                ClientId = "fixture-client",
                ClientSecret = "fixture-secret"
            });
    }

    public static AcronisMicrosoft365InventoryProvider CreateMicrosoft365Provider(
        HttpClient client)
    {
        return new AcronisMicrosoft365InventoryProvider(CreateApiClient(client));
    }

    public static AcronisTenantProvider CreateTenantProvider(HttpClient client)
    {
        return new AcronisTenantProvider(CreateApiClient(client));
    }

    public static AcronisCursorPaginator CreatePaginator(HttpClient client)
    {
        return new AcronisCursorPaginator(CreateApiClient(client));
    }

    public static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage TokenResponse(
        AuthenticationHeaderValue? authorization)
    {
        var token = string.Equals(
            authorization?.Scheme,
            "Basic",
            StringComparison.OrdinalIgnoreCase)
            ? "base-token"
            : "scoped-token";

        return JsonResponse(
            $$"""
            {
              "access_token": "{{token}}",
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
