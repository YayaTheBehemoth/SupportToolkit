using System.Net;
using SupportToolkit.Providers.Acronis.Usage;
using static SupportToolkit.Tests.Providers.Acronis.AcronisProviderTestSupport;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisUsageProviderTests
{
    private const string TenantA = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string TenantB = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    [Fact]
    public async Task ParsesUsageAsReported_WithoutConvertingUnitsOrInventingValues()
    {
        var requests = new List<string>();

        using var client =
            CreateClient(
                request =>
                {
                    requests.Add(request.RequestUri!.PathAndQuery);

                    return JsonResponse(
                        $$"""
                        {
                          "items": [
                            {
                              "tenant": "{{TenantA}}",
                              "usages": [
                                {
                                  "name": "storage",
                                  "usage_name": "storage",
                                  "edition": "standard",
                                  "type": "infra",
                                  "measurement_unit": "bytes",
                                  "value": 1048576,
                                  "absolute_value": 2097152,
                                  "range_start": "2026-10-01T00:00:00"
                                },
                                {
                                  "usage_name": "local_storage",
                                  "edition": "standard",
                                  "value": null
                                }
                              ]
                            }
                          ]
                        }
                        """);
                });

        var result =
            await new AcronisUsageProvider(CreateApiClient(client))
                .GetUsagesAsync([TenantA, TenantB]);

        var request = Assert.Single(requests);
        Assert.StartsWith("/api/2/tenants/usages?tenants=", request);
        Assert.Contains(TenantA, request);
        Assert.Contains(TenantB, request);

        var rows = result.ByTenant[TenantA];
        Assert.Equal(2, rows.Count);

        var storage = rows.Single(row => row.MetricName == "storage");
        Assert.Equal(1048576m, storage.Value);
        Assert.Equal(2097152m, storage.AbsoluteValue);
        Assert.Equal("bytes", storage.Unit);
        Assert.Equal("2026-10-01T00:00:00", storage.RangeStart);

        var local = rows.Single(row => row.MetricName == "local_storage");
        Assert.Null(local.Value);
        Assert.Null(local.Unit);

        // Nothing returned for the second tenant: absent, not zero.
        Assert.False(result.ByTenant.ContainsKey(TenantB));
    }

    [Fact]
    public async Task LargeRequests_AreChunked()
    {
        var requests = new List<string>();

        using var client =
            CreateClient(
                request =>
                {
                    requests.Add(request.RequestUri!.Query);
                    return JsonResponse("""{ "items": [] }""");
                });

        var ids =
            Enumerable.Range(1, 120)
                .Select(index => new Guid(index, 0, 0, new byte[8]).ToString())
                .ToArray();

        await new AcronisUsageProvider(CreateApiClient(client))
            .GetUsagesAsync(ids);

        Assert.Equal(3, requests.Count);

        Assert.Equal(
            [50, 50, 20],
            requests.Select(
                query => query["?tenants=".Length..].Split(',').Length));
    }

    [Fact]
    public async Task AFailedChunk_FailsTheWholeRead_NeverAPartialResult()
    {
        var calls = 0;

        using var client =
            CreateClient(
                _ => ++calls == 1
                    ? JsonResponse("""{ "items": [] }""")
                    : new HttpResponseMessage(
                        HttpStatusCode.InternalServerError));

        var ids =
            Enumerable.Range(1, 60)
                .Select(index => new Guid(index, 0, 0, new byte[8]).ToString())
                .ToArray();

        await Assert.ThrowsAsync<HttpRequestException>(
            () => new AcronisUsageProvider(CreateApiClient(client))
                .GetUsagesAsync(ids));
    }

    [Fact]
    public async Task UnrequestedTenants_AreCountedNotAdded()
    {
        using var client =
            CreateClient(
                _ => JsonResponse(
                    $$"""
                    {
                      "items": [
                        { "tenant": "{{TenantB}}", "usages": [ { "usage_name": "storage" } ] }
                      ]
                    }
                    """));

        var result =
            await new AcronisUsageProvider(CreateApiClient(client))
                .GetUsagesAsync([TenantA]);

        Assert.Empty(result.ByTenant);
        Assert.Equal(1, result.UnrequestedItems);
    }

    [Fact]
    public async Task InvalidTenantId_IsRefusedBeforeAnyRequest()
    {
        var calls = 0;

        using var client =
            CreateClient(
                _ =>
                {
                    calls++;
                    return JsonResponse("""{ "items": [] }""");
                });

        await Assert.ThrowsAsync<ArgumentException>(
            () => new AcronisUsageProvider(CreateApiClient(client))
                .GetUsagesAsync(["12345"]));

        Assert.Equal(0, calls);
    }
}
