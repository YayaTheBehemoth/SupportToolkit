
using System.Net;
using System.Text;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisCursorPaginatorSafetyTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public async Task RepeatedCursor_FailsWithoutFurtherRequests(
        bool includeIntermediateCursor,
        int expectedRequests)
    {
        var requests = 0;

        using var client =
            CreateClient(() =>
            {
                requests++;

                var cursor =
                    requests switch
                    {
                        1 => "opaque-cursor-a",

                        2 when includeIntermediateCursor =>
                            "opaque-cursor-b",

                        _ => "opaque-cursor-a"
                    };

                return Page(requests, cursor);
            });

        var paginator =
            CreatePaginator(client);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => FetchTenantsAsync(paginator)
            );

        Assert.Contains(
            "repeated continuation cursor",
            exception.Message
        );

        Assert.DoesNotContain(
            "opaque-cursor-a",
            exception.Message
        );

        Assert.DoesNotContain(
            "opaque-cursor-b",
            exception.Message
        );

        Assert.Equal(
            expectedRequests,
            requests
        );
    }

    [Fact]
    public async Task DistinctCursors_ReturnAllPages()
    {
        var requests = 0;

        using var client =
            CreateClient(() =>
            {
                requests++;

                return requests switch
                {
                    1 => Page(1, "cursor-a"),
                    2 => Page(2, "cursor-b"),
                    _ => Page(3, null)
                };
            });

        var paginator =
            CreatePaginator(client);

        var tenants =
            await FetchTenantsAsync(paginator);

        Assert.Equal(3, requests);
        Assert.Equal(3, tenants.Count);

        Assert.Equal(
            ["Synthetic tenant 1", "Synthetic tenant 2", "Synthetic tenant 3"],
            tenants.Select(x => x.Name).ToArray()
        );
    }

    [Fact]
    public async Task ContinuouslyUniqueCursors_AreBounded()
    {
        var requests = 0;

        using var client =
            CreateClient(() =>
            {
                requests++;

                return Page(
                    requests,
                    $"cursor-{requests}"
                );
            });

        var paginator =
            CreatePaginator(client);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => FetchTenantsAsync(paginator)
            );

        Assert.Contains(
            "exceeded 500 pages",
            exception.Message
        );

        Assert.Equal(500, requests);
    }

    private static Task<IReadOnlyList<TenantDto>>
        FetchTenantsAsync(
            AcronisCursorPaginator paginator)
    {
        return paginator.FetchAllAsync<TenantPageDto, TenantDto>(
            datasetName: "tenants",
            firstRequestPath: "/api/2/tenants?limit=100",
            buildNextRequestPath:
                after =>
                    "/api/2/tenants?limit=100&after=" +
                    Uri.EscapeDataString(after),
            getItems: page => page.Items,
            getAfterCursor: page => page.Paging.Cursors.After
        );
    }

    private static AcronisCursorPaginator CreatePaginator(
        HttpClient client)
    {
        var apiClient =
            new AcronisApiClient(
                client,
                new AcronisOptions
                {
                    DatacenterUrl =
                        "https://fixture.acronis.invalid",
                    ClientId = "fixture-client",
                    ClientSecret = "fixture-secret"
                }
            );

        return new AcronisCursorPaginator(apiClient);
    }

    private static HttpClient CreateClient(
        Func<HttpResponseMessage> nextPage)
    {
        return new HttpClient(
            new StubHttpMessageHandler(
                request =>
                {
                    var path =
                        request.RequestUri?.AbsolutePath;

                    if (path == "/api/2/idp/token")
                    {
                        return JsonResponse(
                            """
                            {
                              "access_token": "fixture-token",
                              "token_type": "bearer",
                              "expires_on": 4102444800
                            }
                            """
                        );
                    }

                    if (path == "/api/2/tenants")
                    {
                        return nextPage();
                    }

                    return new HttpResponseMessage(
                        HttpStatusCode.NotFound
                    );
                }
            )
        );
    }

    private static HttpResponseMessage Page(
        int number,
        string? after)
    {
        var cursors =
            after is null
                ? "{}"
                : $$"""
                  {
                    "after": "{{after}}"
                  }
                  """;

        return JsonResponse(
            $$"""
            {
              "paging": {
                "cursors": {{cursors}}
              },
              "timestamp": "2026-10-01T12:00:00Z",
              "items": [
                {
                  "id": "synthetic-tenant-{{number}}",
                  "name": "Synthetic tenant {{number}}"
                }
              ]
            }
            """
        );
    }

    private static HttpResponseMessage JsonResponse(
        string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json"
                )
        };
    }

    private sealed class StubHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>
            _handler;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _handler(request)
            );
        }
    }
}
