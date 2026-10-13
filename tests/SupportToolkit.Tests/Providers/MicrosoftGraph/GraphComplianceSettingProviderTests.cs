
using System.Net;
using System.Text;
using SupportToolkit.Providers.MicrosoftGraph.ComplianceSettings;

namespace SupportToolkit.Tests.Providers.MicrosoftGraph;

public class GraphComplianceSettingProviderTests
{
    [Fact]
    public async Task GetSettingEvidenceAsync_ReadsNestedStates()
    {
        var requestedPaths = new List<string>();

        using var client = CreateClient(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requestedPaths.Add(path);

            Assert.Equal(HttpMethod.Get, request.Method);

            if (path.EndsWith(
                "/deviceCompliancePolicySettingStateSummaries"))
            {
                return JsonResponse(
                    """
                    {
                      "value": [
                        {
                          "id": "synthetic-summary",
                          "settingName": "Device encryption required"
                        }
                      ]
                    }
                    """
                );
            }

            if (path.EndsWith(
                "/synthetic-summary/deviceComplianceSettingStates"))
            {
                return JsonResponse(
                    """
                    {
                      "value": [
                        {
                          "deviceId": "device-001",
                          "deviceName": "SYNTHETIC-01",
                          "settingName": "Device encryption required",
                          "state": "nonCompliant"
                        }
                      ]
                    }
                    """
                );
            }

            return new HttpResponseMessage(
                HttpStatusCode.NotFound
            );
        });

        var provider = new GraphComplianceSettingProvider(
            client
        );

        var result = await provider.GetSettingEvidenceAsync();

        var item = Assert.Single(result);

        Assert.Equal("synthetic-summary", item.SummaryId);
        Assert.Equal("device-001", item.DeviceId);
        Assert.Equal("nonCompliant", item.State);
        Assert.Equal(2, requestedPaths.Count);
    }

    [Fact]
    public async Task GetSettingEvidenceAsync_FollowsSummaryPagination()
    {
        using var client = CreateClient(request =>
        {
            var query = request.RequestUri!.Query;
            var path = request.RequestUri.AbsolutePath;

            if (path.EndsWith(
                "/deviceCompliancePolicySettingStateSummaries"))
            {
                if (query == "?$skiptoken=second")
                {
                    return JsonResponse(
                        """
                        {
                          "value": [
                            {
                              "id": "summary-002",
                              "settingName": "Second setting"
                            }
                          ]
                        }
                        """
                    );
                }

                return JsonResponse(
                    """
                    {
                      "@odata.nextLink":
                        "https://graph.microsoft.com/v1.0/deviceManagement/deviceCompliancePolicySettingStateSummaries?$skiptoken=second",
                      "value": [
                        {
                          "id": "summary-001",
                          "settingName": "First setting"
                        }
                      ]
                    }
                    """
                );
            }

            if (path.Contains("/summary-001/"))
            {
                return JsonResponse(
                    """
                    {
                      "value": [
                        {
                          "deviceId": "device-001",
                          "state": "nonCompliant"
                        }
                      ]
                    }
                    """
                );
            }

            if (path.Contains("/summary-002/"))
            {
                return JsonResponse(
                    """
                    {
                      "value": [
                        {
                          "deviceId": "device-002",
                          "state": "compliant"
                        }
                      ]
                    }
                    """
                );
            }

            return new HttpResponseMessage(
                HttpStatusCode.NotFound
            );
        });

        var provider = new GraphComplianceSettingProvider(
            client
        );

        var result = await provider.GetSettingEvidenceAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(
            result,
            item => item.DeviceId == "device-001"
        );
        Assert.Contains(
            result,
            item => item.DeviceId == "device-002"
        );
    }

    [Fact]
    public async Task GetSettingEvidenceAsync_RejectsExternalNextLink()
    {
        var requests = 0;

        using var client = CreateClient(_ =>
        {
            requests++;

            return JsonResponse(
                """
                {
                  "@odata.nextLink": "https://example.com/other",
                  "value": []
                }
                """
            );
        });

        var provider = new GraphComplianceSettingProvider(
            client
        );

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetSettingEvidenceAsync()
        );

        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task GetSettingEvidenceAsync_PropagatesForbidden()
    {
        using var client = CreateClient(
            _ => new HttpResponseMessage(
                HttpStatusCode.Forbidden
            )
        );

        var provider = new GraphComplianceSettingProvider(
            client
        );

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetSettingEvidenceAsync()
        );
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        return new HttpClient(
            new StubHandler(response)
        );
    }

    private static HttpResponseMessage JsonResponse(
        string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"
            )
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            HttpResponseMessage
        > _response;

        public StubHandler(
            Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _response(request)
            );
        }
    }
}
