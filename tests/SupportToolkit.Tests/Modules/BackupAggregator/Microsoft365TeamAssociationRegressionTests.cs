
using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class Microsoft365TeamAssociationRegressionTests
{
    private readonly Microsoft365BackupResourceNormalizer _normalizer =
        new();

    private static readonly DateTimeOffset SuccessfulBackup =
        DateTimeOffset.Parse("2026-10-08T12:00:00Z");

    [Theory]
    [InlineData(15, 1, 1, 16)]
    [InlineData(136, 5, 0, 136)]
    public void TeamAssociatedGroups_WithProtectedSites_AreSeparateWorkloads(
        int microsoft365Count,
        int associatedGroups,
        int deviceCount,
        int expectedTotal)
    {
        var resources = new List<AcronisMicrosoft365ResourceDto>();

        for (var i = 0; i < associatedGroups; i++)
        {
            resources.Add(CreateResource(
                $"Group {i}",
                "GROUP",
                $"GROUP.pair-{i}",
                teamName: $"Team {i}",
                basicKinds: GroupComponents(true)));

            resources.Add(CreateResource(
                $"Team {i}",
                "TEAM",
                $"TEAM.pair-{i}"));
        }

        for (var i = resources.Count; i < microsoft365Count; i++)
        {
            resources.Add(CreateResource(
                $"User {i}",
                "USER",
                $"USER.synthetic-{i}"));
        }

        var devices = Enumerable.Range(0, deviceCount)
            .Select(i => new AcronisDeviceResourceDto
            {
                Id = $"device-{i}",
                Name = $"Synthetic device {i}",
                Status = new AcronisDeviceResourceStatusDto
                {
                    State = "idle",
                    LastBackup = SuccessfulBackup,
                    LastSuccessBackup = SuccessfulBackup
                }
            })
            .ToList();

        var report = new BackupInventoryNormalizer()
            .BuildTenantReport(
                "Synthetic tenant",
                resources,
                devices);

        Assert.Equal(expectedTotal, report.RowsChecked);
        Assert.Equal(expectedTotal, report.HealthyRowsSuppressed);
        Assert.Equal(0, report.FindingsCount);
        Assert.Equal(0, report.UnknownRowsCount);
    }

    [Theory]
    [InlineData(16, 4, 24, 5, 5, false, 44)]
    [InlineData(39, 19, 72, 6, 5, true, 136)]
    public void ObservedTenantShapes_MatchExpectedWorkloadCounts(
        int groupCount,
        int siteCount,
        int userCount,
        int teamCount,
        int associatedGroups,
        bool independentSiteProtection,
        int expectedRows)
    {
        var resources = new List<AcronisMicrosoft365ResourceDto>();

        for (var i = 0; i < groupCount; i++)
        {
            var associated = i < associatedGroups;

            bool? siteProtection =
                !associated || independentSiteProtection
                    ? true
                    : null;

            resources.Add(CreateResource(
                $"Group {i}",
                "GROUP",
                associated
                    ? $"GROUP.pair-{i}"
                    : $"GROUP.other-{i}",
                teamName: associated ? $"Team {i}" : null,
                basicKinds: GroupComponents(siteProtection)));
        }

        for (var i = 0; i < siteCount; i++)
        {
            resources.Add(CreateResource(
                $"Site {i}",
                "SITE",
                $"SITE.site-{i}"));
        }

        for (var i = 0; i < userCount; i++)
        {
            resources.Add(CreateResource(
                $"User {i}",
                "USER",
                $"USER.user-{i}"));
        }

        for (var i = 0; i < teamCount; i++)
        {
            resources.Add(CreateResource(
                $"Team {i}",
                "TEAM",
                i < associatedGroups
                    ? $"TEAM.pair-{i}"
                    : $"TEAM.unrelated-{i}"));
        }

        var report = new BackupInventoryNormalizer()
            .BuildTenantReport(
                "Synthetic tenant",
                resources,
                []);

        Assert.Equal(
            groupCount + siteCount + userCount + teamCount,
            resources.Count);

        Assert.Equal(expectedRows, report.RowsChecked);
        Assert.Equal(expectedRows, report.HealthyRowsSuppressed);
        Assert.Equal(0, report.FindingsCount);
        Assert.Equal(0, report.UnknownRowsCount);
    }

    [Fact]
    public void MailboxOnlyGroupFailure_IsNotSuppressed()
    {
        var resources = new[]
        {
            CreateResource(
                "Example group",
                "GROUP",
                "GROUP.shared",
                teamName: "Example team",
                status: "warning",
                basicKinds: GroupComponents(null)),

            CreateResource(
                "Example team",
                "TEAM",
                "TEAM.shared")
        };

        var entry = Assert.Single(_normalizer.Normalize(resources));

        Assert.Equal("Example team", entry.ResourceName);
        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification);
        Assert.Equal("warning", entry.LastResult);
    }

    [Fact]
    public void IndependentlyProtectedGroupFailure_RemainsSeparate()
    {
        var resources = new[]
        {
            CreateResource(
                "Example group",
                "GROUP",
                "GROUP.shared",
                teamName: "Example team",
                status: "warning",
                basicKinds: GroupComponents(true)),

            CreateResource(
                "Example team",
                "TEAM",
                "TEAM.shared")
        };

        var entries = _normalizer.Normalize(resources);

        Assert.Equal(2, entries.Count);

        var group = Assert.Single(
            entries,
            entry => entry.ResourceName == "Example group");

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            group.Classification);
        Assert.Equal("warning", group.LastResult);

        Assert.Contains(
            entries,
            entry =>
                entry.ResourceName == "Example team"
                && entry.Classification
                    == BackupReportEntryClassification.Healthy);
    }

    [Fact]
    public void AssociatedGroupWithoutProtectionEvidence_IsPreserved()
    {
        var resources = new[]
        {
            CreateResource(
                "Example group",
                "GROUP",
                "GROUP.shared",
                teamName: "Example team",
                protectedResource: null),

            CreateResource(
                "Example team",
                "TEAM",
                "TEAM.shared")
        };

        var entries = _normalizer.Normalize(resources);

        Assert.Equal(2, entries.Count);

        var group = Assert.Single(
            entries,
            entry => entry.ResourceName == "Example group");

        Assert.Equal(
            BackupReportEntryClassification.Unknown,
            group.Classification);
    }

    [Fact]
    public void ExplicitlyUnprotectedSite_RemainsVisible()
    {
        var resources = new[]
        {
            CreateResource(
                "Example group",
                "GROUP",
                "GROUP.shared",
                teamName: "Example team",
                basicKinds: GroupComponents(false)),

            CreateResource(
                "Example team",
                "TEAM",
                "TEAM.shared")
        };

        var entries = _normalizer.Normalize(resources);

        Assert.Equal(2, entries.Count);

        var group = Assert.Single(
            entries,
            entry => entry.ResourceName == "Example group");

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            group.Classification);
        Assert.Equal("protection_conflict", group.LastResult);
    }

    [Fact]
    public void MailboxOnlyGroupWithoutMatchingTeam_IsPreserved()
    {
        var resources = new[]
        {
            CreateResource(
                "Example group",
                "GROUP",
                "GROUP.group-one",
                teamName: "Example team",
                basicKinds: GroupComponents(null)),

            CreateResource(
                "Example team",
                "TEAM",
                "TEAM.team-two")
        };

        Assert.Equal(2, _normalizer.Normalize(resources).Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GroupWithoutTeamName_UsesLegacyConsolidation(
        string? teamName)
    {
        var resources = new[]
        {
            CreateResource(
                "Example group",
                "GROUP",
                "GROUP.shared",
                teamName: teamName,
                status: "warning"),

            CreateResource(
                "Example team",
                "TEAM",
                "TEAM.shared")
        };

        var entry = Assert.Single(_normalizer.Normalize(resources));

        Assert.Equal("Example team", entry.ResourceName);
        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification);
        Assert.Equal("warning", entry.LastResult);
    }

    private static IReadOnlyList<AcronisMicrosoft365BasicKindDto?>
        GroupComponents(bool? siteProtected)
    {
        return
        [
            new AcronisMicrosoft365BasicKindDto
            {
                Kind = "SITE",
                HasProtections = siteProtected
            },
            new AcronisMicrosoft365BasicKindDto
            {
                Kind = "MAILBOX",
                HasProtections = true
            }
        ];
    }

    private static AcronisMicrosoft365ResourceDto CreateResource(
        string name,
        string kind,
        string internalId,
        string? teamName = null,
        string status = "ok",
        bool? protectedResource = true,
        IReadOnlyList<AcronisMicrosoft365BasicKindDto?>? basicKinds = null)
    {
        return new AcronisMicrosoft365ResourceDto
        {
            Id = Guid.NewGuid().ToString(),
            InternalId = internalId,
            Name = name,
            Kind = kind,
            TeamName = teamName,
            BasicKinds = basicKinds,
            HasProtections = protectedResource,
            LastTaskStatus = status,
            LastTaskState = "idle",
            LastSuccessTime = SuccessfulBackup,
            LastFinishTime = SuccessfulBackup
        };
    }
}
