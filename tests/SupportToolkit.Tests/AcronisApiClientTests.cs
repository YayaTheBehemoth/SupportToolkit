using System.Net;
using System.Text;
using SupportToolkit.Providers.Acronis;

namespace SupportToolkit.Tests;

public class AcronisApiClientTests
{
    [Fact]
    public async Task GetRootTenantIdAsync_AuthenticatesAndReturnsTenantId()
    {
        var handler = new StubHttpMessageHandler(
            async request =>
            {
                if (request.RequestUri?.AbsolutePath
                    == "/api/2/idp/token")
                {
                    Assert.Equal(
                        HttpMethod.Post,
                        request.Method
                    );

                    Assert.Equal(
                        "Basic",
                        request.Headers.Authorization?.Scheme
                    );

                    var expectedCredentials =
                        Convert.ToBase64String(
                            Encoding.ASCII.GetBytes(
                                "fixture-client:fixture-secret"
                            )
                        );

                    Assert.Equal(
                        expectedCredentials,
                        request.Headers.Authorization?.Parameter
                    );

                    var body =
                        await request.Content!.ReadAsStringAsync();

                    Assert.Contains(
                        "grant_type=client_credentials",
                        body
                    );

                    return JsonResponse(
                        """
                        {
                          "access_token": "fixture-access-token",
                          "token_type": "bearer",
                          "expires_on": 1790859600
                        }
                        """
                    );
                }

                if (request.RequestUri?.AbsolutePath
                    == "/api/2/clients/fixture-client")
                {
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
        );

        var httpClient =
            new HttpClient(handler);

        var options = new AcronisOptions
        {
            DatacenterUrl =
                "https://fixture.acronis.invalid",

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
            await apiClient.GetRootTenantIdAsync();

        Assert.Equal(
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            tenantId
        );
    }

    private static HttpResponseMessage JsonResponse(
        string json)
    {
        return new HttpResponseMessage(
            HttpStatusCode.OK
        )
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private sealed class StubHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            Task<HttpResponseMessage>
        > _handler;

        public StubHttpMessageHandler(
            Func<
                HttpRequestMessage,
                Task<HttpResponseMessage>
            > handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}