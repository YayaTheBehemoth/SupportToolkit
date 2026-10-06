using System.Net;
using System.Text;
using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Tests;

public class AcronisApiClientReadOnlyIntegrationTests
{
    private const string DatacenterUrl =
        "https://fixture.acronis.invalid";

    [Fact]
    public async Task ApiClient_AuthenticatesAndReadsThroughReadOnlyGuard()
    {
        var network =
            new RecordingAcronisHandler();

        var readOnlyHandler =
            new AcronisReadOnlyHandler(
                DatacenterUrl,
                network
            );

        using var httpClient =
            new HttpClient(
                readOnlyHandler
            );

        var options =
            new AcronisOptions
            {
                DatacenterUrl =
                    DatacenterUrl,

                ClientId =
                    "fixture-client",

                ClientSecret =
                    "fixture-secret"
            };

        var apiClient =
            new AcronisApiClient(
                httpClient,
                options
            );

        var tenantId =
            await apiClient
                .GetRootTenantIdAsync();

        Assert.Equal(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            tenantId
        );

        Assert.Equal(
            2,
            network.RequestCount
        );

        Assert.Equal(
            [
                "POST /api/2/idp/token",
                "GET /api/2/clients/fixture-client"
            ],
            network.Requests
        );
    }

    [Fact]
    public async Task ApiClient_UnapprovedGet_IsBlockedBeforeNetworkTransmission()
    {
        var network =
            new RecordingAcronisHandler();

        var readOnlyHandler =
            new AcronisReadOnlyHandler(
                DatacenterUrl,
                network
            );

        using var httpClient =
            new HttpClient(
                readOnlyHandler
            );

        var options =
            new AcronisOptions
            {
                DatacenterUrl =
                    DatacenterUrl,

                ClientId =
                    "fixture-client",

                ClientSecret =
                    "fixture-secret"
            };

        var apiClient =
            new AcronisApiClient(
                httpClient,
                options
            );

        /*
         * Perform one legitimate request first.
         *
         * This authenticates the client and caches the token, which means the
         * following blocked request cannot be confused with token acquisition
         * traffic.
         */
        var tenantId =
            await apiClient
                .GetRootTenantIdAsync();

        Assert.Equal(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            tenantId
        );

        Assert.Equal(
            2,
            network.RequestCount
        );

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    apiClient.GetAsync(
                        "/api/not-approved"
                    )
            );

        Assert.Contains(
            "read-only",
            exception.Message,
            StringComparison.OrdinalIgnoreCase
        );

        /*
         * The network saw only the legitimate authentication request and
         * legitimate client-metadata request.
         *
         * The rejected endpoint never crossed the transport boundary.
         */
        Assert.Equal(
            2,
            network.RequestCount
        );

        Assert.DoesNotContain(
            network.Requests,
            request =>
                request.Contains(
                    "/api/not-approved",
                    StringComparison.Ordinal
                )
        );
    }

    private sealed class RecordingAcronisHandler
        : HttpMessageHandler
    {
        public int RequestCount =>
            Requests.Count;

        public List<string> Requests { get; } =
            [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path =
                request.RequestUri?.AbsolutePath
                ?? string.Empty;

            Requests.Add(
                $"{request.Method.Method} {path}"
            );

            if (path == "/api/2/idp/token")
            {
                Assert.Equal(
                    HttpMethod.Post,
                    request.Method
                );

                return JsonResponse(
                    """
                    {
                      "access_token": "fixture-access-token",
                      "token_type": "bearer",
                      "expires_on": 4102444800
                    }
                    """
                );
            }

            if (path
                == "/api/2/clients/fixture-client")
            {
                Assert.Equal(
                    HttpMethod.Get,
                    request.Method
                );

                Assert.Equal(
                    "Bearer",
                    request.Headers.Authorization?.Scheme
                );

                Assert.Equal(
                    "fixture-access-token",
                    request.Headers.Authorization?.Parameter
                );

                return JsonResponse(
                    """
                    {
                      "tenant_id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                      "type": "api_client"
                    }
                    """
                );
            }

            return new HttpResponseMessage(
                HttpStatusCode.NotFound
            );
        }

        private static HttpResponseMessage JsonResponse(
            string json)
        {
            return new HttpResponseMessage(
                HttpStatusCode.OK
            )
            {
                Content =
                    new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"
                    )
            };
        }
    }
}