
using System.Net;
using System.Text.Json;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;
using SupportToolkit.Providers.Acronis.Transport;

namespace SupportToolkit.Tests.Providers.Acronis;

public class AcronisLegacyHierarchyContractSafetyTests
{
    private const string RootUuid =
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Fact]
    public void MissingHasChildren_FailsDeserialization()
    {
        const string json =
            """
            {
              "items": [
                {
                  "id": 1001,
                  "uuid": "11111111-1111-1111-1111-111111111111"
                }
              ]
            }
            """;

        Assert.Throws<JsonException>(
            () => Deserialize(json));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("true")]
    public void InvalidHasChildrenType_FailsDeserialization(
        string value)
    {
        var json =
            $$"""
            {
              "items": [
                {
                  "id": 1001,
                  "uuid": "11111111-1111-1111-1111-111111111111",
                  "has_children": {{value}}
                }
              ]
            }
            """;

        Assert.Throws<JsonException>(
            () => Deserialize(json));
    }

    [Fact]
    public void MissingItems_FailsDeserialization()
    {
        Assert.Throws<JsonException>(
            () => Deserialize("{}"));
    }

    [Fact]
    public void EmptyItems_RemainsValid()
    {
        var page =
            Deserialize(
                """
                {
                  "items": []
                }
                """);

        Assert.NotNull(page);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task NullItems_FailsMapping()
    {
        var fixture =
            CreateProvider(
                """
                {
                  "items": null
                }
                """);

        using var client = fixture.Client;

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.Provider.GetTenantIdMappingsAsync());

        Assert.Contains(
            "null 'items' collection",
            exception.Message);
    }

    [Fact]
    public async Task NegativeHasChildren_FailsMapping()
    {
        var fixture =
            CreateProvider(
                """
                {
                  "items": [
                    {
                      "id": 1001,
                      "uuid": "11111111-1111-1111-1111-111111111111",
                      "has_children": -1
                    }
                  ]
                }
                """);

        using var client = fixture.Client;

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.Provider.GetTenantIdMappingsAsync());

        Assert.Contains(
            "invalid 'has_children' value",
            exception.Message);
    }

    private static LegacyTenantChildrenPageDto? Deserialize(
        string json)
    {
        return JsonSerializer.Deserialize<LegacyTenantChildrenPageDto>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static (
        AcronisTenantProvider Provider,
        HttpClient Client
    ) CreateProvider(string childrenJson)
    {
        var client =
            AcronisProviderTestSupport.CreateClient(
                    request =>
                    {
                        var path =
                            request.RequestUri?.AbsolutePath
                            ?? string.Empty;

                        if (path == "/api/2/clients/fixture-client")
                        {
                            return AcronisProviderTestSupport.JsonResponse(
                                $$"""
                                {
                                  "tenant_id": "{{RootUuid}}",
                                  "type": "api_client"
                                }
                                """);
                        }

                        if (path == $"/api/1/groups/{RootUuid}")
                        {
                            return AcronisProviderTestSupport.JsonResponse(
                                $$"""
                                {
                                  "id": 9000,
                                  "uuid": "{{RootUuid}}"
                                }
                                """);
                        }

                        if (path == "/api/1/groups/9000/children")
                        {
                            return AcronisProviderTestSupport.JsonResponse(childrenJson);
                        }

                        return new HttpResponseMessage(
                            HttpStatusCode.NotFound);
                    });

        var provider = AcronisProviderTestSupport.CreateTenantProvider(client);

        return (provider, client);
    }

}
