using SupportToolkit.Composition;
using System.Text.Json.Nodes;
using SupportToolkit.Core.Configuration;
using SupportToolkit.Tests.Ui.Web;

namespace SupportToolkit.Tests.Core.Configuration;

/// <summary>
/// Saving the configuration must never discard a setting this build does
/// not recognise. An older build once did exactly that to a newer build's
/// connections, silently, on a profile switch.
/// </summary>
public class ConfigurationPreservationTests
{
    /// <summary>
    /// A file as a newer build might write it: every object carries a
    /// setting this build has no property for.
    /// </summary>
    private const string FutureJson = """
        {
          "activeProfile": "work",
          "futureRootSetting": { "nested": [1, 2, { "deep": true }] },
          "connections": {
            "futureConnectionKind": {
              "main": { "endpoint": "https://example.invalid", "retries": 3 }
            },
            "acronis": {
              "acr": {
                "datacenterUrl": "https://example.acronis.invalid",
                "clientId": "example-client",
                "futureAcronisSetting": [ "a", "b" ]
              }
            },
            "zendesk": {
              "zd": {
                "subdomain": "example",
                "clientId": "example-client",
                "futureZendeskSetting": 1.50,
                "destinations": {
                  "dev": { "groupId": 11, "futureDestinationSetting": "keep" }
                }
              }
            },
            "zendeskDeletion": {
              "del": {
                "subdomain": "example",
                "clientId": "example-public-client",
                "futureDeletionSetting": { "scopes": [ "read" ] }
              }
            },
            "businessCentral": {
              "bc": {
                "tenantId": "00000000-0000-0000-0000-000000000001",
                "clientId": "00000000-0000-0000-0000-000000000002",
                "environment": "example-environment",
                "companyId": "00000000-0000-0000-0000-000000000003",
                "apiPublisher": "example",
                "apiGroup": "subscriptions",
                "apiVersion": "v1.0",
                "futureBusinessCentralSetting": false
              }
            }
          },
          "profiles": {
            "work": {
              "acronisConnection": "acr",
              "futureProfileSetting": 12345678901234567890,
              "monitoringScope": {
                "backup-aggregator": {
                  "monitoringItemNumber": "EXAMPLE-ITEM",
                  "futureScopeSetting": "kept"
                }
              },
              "review": {
                "backup-aggregator": {
                  "inProgressMaxWaitSeconds": 60,
                  "futureReviewSetting": [ ]
                }
              },
              "modules": { "backup-aggregator": "fixture" }
            },
            "other": {
              "modules": { "backup-aggregator": "fixture" }
            }
          }
        }
        """;

    private static JsonNode Parse(string json) =>
        JsonNode.Parse(json)!;

    private static string NewDirectory() =>
        Directory.CreateTempSubdirectory("st-config-keep-").FullName;

    private static async Task<(JsonNode Original, JsonNode Saved)>
        RoundTripAsync(string json)
    {
        var directory = NewDirectory();

        try
        {
            var path = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(path, json);

            var store = new JsonSupportToolkitConfigurationStore(path);

            await store.SaveAsync(await store.LoadAsync());

            return (Parse(json), Parse(await File.ReadAllTextAsync(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("futureRootSetting")]
    [InlineData("connections.futureConnectionKind")]
    [InlineData("connections.acronis.acr.futureAcronisSetting")]
    [InlineData("connections.zendesk.zd.futureZendeskSetting")]
    [InlineData("connections.zendesk.zd.destinations.dev.futureDestinationSetting")]
    [InlineData("connections.zendeskDeletion.del.futureDeletionSetting")]
    [InlineData("connections.businessCentral.bc.futureBusinessCentralSetting")]
    [InlineData("profiles.work.futureProfileSetting")]
    [InlineData("profiles.work.monitoringScope.backup-aggregator.futureScopeSetting")]
    [InlineData("profiles.work.review.backup-aggregator.futureReviewSetting")]
    public async Task ASettingThisBuildDoesNotKnow_SurvivesALoadAndSave(
        string path)
    {
        var (original, saved) = await RoundTripAsync(FutureJson);

        var before = Dig(original, path);
        var after = Dig(saved, path);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.True(
            JsonNode.DeepEquals(before, after),
            $"{path} changed: {before!.ToJsonString()} became " +
            $"{after!.ToJsonString()}");
    }

    [Fact]
    public async Task NumbersAreKeptExactly_NotRoundedOrReformatted()
    {
        var (_, saved) = await RoundTripAsync(FutureJson);

        var text = saved.ToJsonString();

        Assert.Contains("12345678901234567890", text);
        Assert.Contains("1.50", text);
    }

    [Fact]
    public async Task KnownSettings_AreNotDuplicatedOrChanged()
    {
        var (original, saved) = await RoundTripAsync(FutureJson);

        Assert.Equal("work", saved["activeProfile"]!.GetValue<string>());

        Assert.Equal(
            "acr",
            saved["profiles"]!["work"]!["acronisConnection"]!
                .GetValue<string>());

        Assert.Equal(
            "https://example.acronis.invalid",
            saved["connections"]!["acronis"]!["acr"]!["datacenterUrl"]!
                .GetValue<string>());

        Assert.Equal(
            60,
            saved["profiles"]!["work"]!["review"]!["backup-aggregator"]!
                ["inProgressMaxWaitSeconds"]!.GetValue<int>());
    }

    [Fact]
    public async Task TheMechanism_IsNotWrittenAsASetting()
    {
        var (_, saved) = await RoundTripAsync(FutureJson);

        Assert.DoesNotContain(
            "unrecognizedSettings",
            saved.ToJsonString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ARetiredSetting_IsStillDropped_OnPurpose()
    {
        const string Json = """
            {
              "activeProfile": "work",
              "profiles": {
                "work": {
                  "allowWrites": false,
                  "futureProfileSetting": "kept",
                  "modules": {
                    "backup-aggregator": "fixture",
                    "backup-health": "fixture"
                  }
                }
              }
            }
            """;

        var (_, saved) = await RoundTripAsync(Json);

        var work = saved["profiles"]!["work"]!;

        // Retired on purpose: dropped.
        Assert.Null(work["allowWrites"]);
        Assert.Null(work["modules"]!["backup-health"]);

        // Everything else in the same profile stays.
        Assert.Equal("kept", work["futureProfileSetting"]!.GetValue<string>());
        Assert.Equal("fixture", work["modules"]!["backup-aggregator"]!.GetValue<string>());
    }

    [Fact]
    public async Task ASettingWrittenAsNull_IsKeptToo()
    {
        const string Json = """
            {
              "activeProfile": "work",
              "futureNullSetting": null,
              "profiles": { "work": { "futureNull": null } }
            }
            """;

        var (_, saved) = await RoundTripAsync(Json);

        Assert.True(((JsonObject)saved).ContainsKey("futureNullSetting"));
        Assert.True(((JsonObject)saved["profiles"]!["work"]!).ContainsKey("futureNull"));
    }

    [Fact]
    public async Task SwitchingProfile_KeepsEverythingElse_EvenWhatThisBuildDoesNotKnow()
    {
        // The incident: a profile switch re-saved the whole file.
        var directory = NewDirectory();

        try
        {
            var path = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(path, FutureJson);

            var store = new JsonSupportToolkitConfigurationStore(path);
            var secrets = new StatusTestSupport.MemorySecrets([]);

            var service =
                new ConfigurationStatusService(
                    store,
                    new SupportToolkitConfigurationResolver(
                        store,
                        secrets,
                        _ => null),
                    secrets,
                    _ => null);

            var result = await service.SetActiveProfileAsync("other");

            Assert.True(result.Succeeded);

            var saved = Parse(await File.ReadAllTextAsync(path));
            var original = Parse(FutureJson);

            Assert.Equal("other", saved["activeProfile"]!.GetValue<string>());

            // Everything that was in the file is still there, unchanged,
            // except the one thing that was meant to change. (A save may
            // add empty containers such as "review": {}; that is
            // normalisation, not a change to anything written.)
            ((JsonObject)original).Remove("activeProfile");

            var missing = Missing(original, saved, "");

            Assert.True(
                missing.Count == 0,
                "The profile switch lost or changed: " +
                string.Join(", ", missing));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AFileWithNothingUnknown_IsUnchangedByASave()
    {
        const string Json = """
            {
              "activeProfile": "work",
              "profiles": { "work": { "modules": { "backup-aggregator": "fixture" } } }
            }
            """;

        var (original, saved) = await RoundTripAsync(Json);

        Assert.Equal("work", saved["activeProfile"]!.GetValue<string>());
        Assert.Equal(
            original["profiles"]!["work"]!["modules"]!["backup-aggregator"]!
                .GetValue<string>(),
            saved["profiles"]!["work"]!["modules"]!["backup-aggregator"]!
                .GetValue<string>());
    }

    /// <summary>
    /// Paths in expected that are missing from, or differ in, actual.
    /// Extra content in actual is fine.
    /// </summary>
    private static List<string> Missing(
        JsonNode expected,
        JsonNode actual,
        string path)
    {
        var problems = new List<string>();

        if (expected is JsonObject expectedObject)
        {
            if (actual is not JsonObject actualObject)
            {
                problems.Add(path);
                return problems;
            }

            foreach (var (key, value) in expectedObject)
            {
                var child = $"{path}/{key}";

                if (value is null)
                {
                    if (!actualObject.ContainsKey(key))
                    {
                        problems.Add(child);
                    }
                }
                else if (!actualObject.TryGetPropertyValue(key, out var found)
                         || found is null)
                {
                    problems.Add(child);
                }
                else
                {
                    problems.AddRange(Missing(value, found, child));
                }
            }

            return problems;
        }

        if (!JsonNode.DeepEquals(expected, actual))
        {
            problems.Add(path);
        }

        return problems;
    }

    /// <summary>Follows a dotted path; a segment may contain dashes.</summary>
    private static JsonNode? Dig(JsonNode root, string path)
    {
        var node = root;

        foreach (var segment in path.Split('.'))
        {
            node = node?[segment];
        }

        return node;
    }
}
