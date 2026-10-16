
using System.Text.Json;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Tests.Providers.Acronis.Contracts;

public class Microsoft365InventoryContractTests
{
    [Theory]
    [InlineData("""{"data":[]}""")]
    [InlineData("""{"items":[]}""")]
    [InlineData("""{"data":null}""")]
    [InlineData("""{"items":null}""")]
    [InlineData("""{"data":null,"items":null}""")]
    public void Groups_ExplicitlyEmptyCollections_AreSupported(
        string json)
    {
        var payload =
            JsonSerializer.Deserialize<AcronisMicrosoft365GroupsDto>(
                json
            );

        Assert.NotNull(payload);
        Assert.Empty(payload.Groups);
    }

    [Fact]
    public void Groups_HandlesNullElementsAndPreservesLeafMetadata()
    {
        const string json =
            """
            {
              "data": [
                null,
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "leaf": true,
                  "groupKind": "synthetic-kind",
                  "unknownField": 123
                }
              ]
            }
            """;

        var payload =
            JsonSerializer.Deserialize<AcronisMicrosoft365GroupsDto>(
                json
            );

        Assert.NotNull(payload);

        var group =
            Assert.Single(payload.Groups);

        Assert.True(group.Leaf);
        Assert.Equal("synthetic-kind", group.GroupKind);

        Assert.True(
            group.AdditionalProperties?.ContainsKey("unknownField")
                == true
        );
    }

    [Fact]
    public void ResourcePage_DeserializesTeamAssociation()
    {
        const string json =
            """
            {
              "items": [
                {
                  "id": "synthetic-group-1",
                  "internalId": "GROUP.synthetic-identity",
                  "kind": "GROUP",
                  "name": "Example group",
                  "teamName": "Example team",
                  "hasProtections": null,
                  "lastTaskStatus": "ok",
                  "lastTaskState": "idle",
                  "lastSuccessTime": "2026-10-08T12:00:00Z",
                  "vendorSpecificField": {
                    "enabled": true
                  }
                }
              ],
              "paging": {
                "cursors": {}
              }
            }
            """;

        var payload =
            JsonSerializer.Deserialize<AcronisMicrosoft365ResourcePageDto>(
                json
            );

        Assert.NotNull(payload);

        var resource =
            Assert.Single(payload.Items!);

        Assert.NotNull(resource);

        Assert.Equal("GROUP", resource.Kind);
        Assert.Equal("Example team", resource.TeamName);
        Assert.Null(resource.HasProtections);

        Assert.Equal(
            DateTimeOffset.Parse("2026-10-08T12:00:00Z"),
            resource.LastSuccessTime
        );

        Assert.False(
            resource.AdditionalProperties?.ContainsKey("teamName")
                == true
        );

        Assert.True(
            resource.AdditionalProperties?.ContainsKey(
                "vendorSpecificField"
            ) == true
        );
    }

    [Theory]
    [InlineData("""{"kind":"GROUP","teamName":""}""", "")]
    [InlineData("""{"kind":"GROUP","teamName":null}""", null)]
    [InlineData("""{"kind":"GROUP"}""", null)]
    public void Resource_HandlesOptionalTeamAssociation(
        string json,
        string? expected)
    {
        var resource =
            JsonSerializer.Deserialize<AcronisMicrosoft365ResourceDto>(
                json
            );

        Assert.NotNull(resource);
        Assert.Equal(expected, resource.TeamName);
    }

    [Fact]
    public void ResourcePage_SupportsExplicitNullItems()
    {
        const string json =
            """
            {
              "items": null,
              "paging": {
                "cursors": {}
              }
            }
            """;

        var payload =
            JsonSerializer.Deserialize<AcronisMicrosoft365ResourcePageDto>(
                json
            );

        Assert.NotNull(payload);
        Assert.Null(payload.Items);
        Assert.NotNull(payload.Paging);
        Assert.Null(payload.Paging.Cursors?.After);
    }

    [Fact]
    public void ResourcePage_PreservesOpaquePaginationCursor()
    {
        const string json =
            """
            {
              "items": [],
              "paging": {
                "cursors": {
                  "after": "synthetic+cursor/next==",
                  "before": "previous"
                }
              },
              "timeStamp": 1700000000
            }
            """;

        var payload =
            JsonSerializer.Deserialize<AcronisMicrosoft365ResourcePageDto>(
                json
            );

        Assert.NotNull(payload);

        Assert.Equal(
            "synthetic+cursor/next==",
            payload.Paging?.Cursors?.After
        );

        Assert.Equal(
            "previous",
            payload.Paging?.Cursors?.Before
        );

        Assert.Equal(1700000000L, payload.TimeStamp);
    }

    [Fact]
    public void Resource_PreservesMixedComponentProtectionEvidence()
    {
        const string json =
            """
            {
              "id": "synthetic-resource",
              "kind": "USER",
              "hasProtections": true,
              "hasCompositeProtections": false,
              "basicKinds": [
                {
                  "kind": "mailbox",
                  "hasProtections": true
                },
                {
                  "kind": "calendar",
                  "hasProtections": false
                },
                null
              ]
            }
            """;

        var resource =
            JsonSerializer.Deserialize<AcronisMicrosoft365ResourceDto>(
                json
            );

        Assert.NotNull(resource);
        Assert.True(resource.HasProtections);
        Assert.False(resource.HasCompositeProtections);

        Assert.NotNull(resource.BasicKinds);
        Assert.Equal(3, resource.BasicKinds.Count);

        Assert.True(resource.BasicKinds[0]?.HasProtections);
        Assert.False(resource.BasicKinds[1]?.HasProtections);
        Assert.Null(resource.BasicKinds[2]);
    }

    [Fact]
    public void Resource_MissingProtectionAndTaskFields_RemainUnknown()
    {
        const string json =
            """
            {
              "id": "synthetic-resource",
              "kind": "FUTURE_VENDOR_KIND",
              "name": "Synthetic resource"
            }
            """;

        var resource =
            JsonSerializer.Deserialize<AcronisMicrosoft365ResourceDto>(
                json
            );

        Assert.NotNull(resource);

        Assert.Equal("FUTURE_VENDOR_KIND", resource.Kind);
        Assert.Null(resource.HasProtections);
        Assert.Null(resource.HasCompositeProtections);
        Assert.Null(resource.BasicKinds);
        Assert.Null(resource.LastTaskStatus);
        Assert.Null(resource.LastTaskState);
        Assert.Null(resource.LastSuccessTime);
    }
}
