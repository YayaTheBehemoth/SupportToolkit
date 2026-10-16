
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
    public void TeamAssociatedGroups_AreAccountedForAsSeparateWorkloads(
        int microsoft365Count,
        int associatedGroups,
        int deviceCount,
        int expectedTotal)
    {
        var microsoft365 =
            new List<AcronisMicrosoft365ResourceDto>();

        for (var index = 0; index < associatedGroups; index++)
        {
            var identity = $"synthetic-{index}";

            microsoft365.Add(
                CreateResource(
                    $"Group {index}",
                    "GROUP",
                    $"GROUP.{identity}",
                    teamName: $"Team {index}"
                )
            );

            microsoft365.Add(
                CreateResource(
                    $"Team {index}",
                    "TEAM",
                    $"TEAM.{identity}"
                )
            );
        }

        var remaining =
            microsoft365Count - microsoft365.Count;

        for (var index = 0; index < remaining; index++)
        {
            microsoft365.Add(
                CreateResource(
                    $"Mailbox {index}",
                    "USER",
                    $"USER.synthetic-{index}"
                )
            );
        }

        var devices =
            Enumerable.Range(0, deviceCount)
                .Select(
                    index =>
                        new AcronisDeviceResourceDto
                        {
                            Id = $"device-{index}",
                            Name = $"Synthetic device {index}",
                            Status = new AcronisDeviceResourceStatusDto
                            {
                                State = "idle",
                                LastBackup = SuccessfulBackup,
                                LastSuccessBackup = SuccessfulBackup
                            }
                        }
                )
                .ToList();

        var report =
            new BackupInventoryNormalizer()
                .BuildTenantReport(
                    "Synthetic tenant",
                    microsoft365,
                    devices
                );

        Assert.Equal(expectedTotal, report.RowsChecked);
        Assert.Equal(expectedTotal, report.HealthyRowsSuppressed);
        Assert.Equal(0, report.FindingsCount);
        Assert.Equal(0, report.UnknownRowsCount);
    }

    [Fact]
    public void AssociatedGroupFailure_IsIndependentFromHealthyTeam()
    {
        var resources =
            new[]
            {
                CreateResource(
                    "Example group",
                    "GROUP",
                    "GROUP.shared-identity",
                    teamName: "Example team",
                    status: "warning"
                ),

                CreateResource(
                    "Example team",
                    "TEAM",
                    "TEAM.shared-identity"
                )
            };

        var entries = _normalizer.Normalize(resources);

        Assert.Equal(2, entries.Count);

        var group =
            Assert.Single(
                entries,
                entry => entry.ResourceName == "Example group"
            );

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            group.Classification
        );

        Assert.Equal("warning", group.LastResult);

        var team =
            Assert.Single(
                entries,
                entry => entry.ResourceName == "Example team"
            );

        Assert.Equal(
            BackupReportEntryClassification.Healthy,
            team.Classification
        );
    }

    [Fact]
    public void AssociatedGroupWithoutProtectionEvidence_IsPreserved()
    {
        var resources =
            new[]
            {
                CreateResource(
                    "Example group",
                    "GROUP",
                    "GROUP.shared-identity",
                    teamName: "Example team",
                    protectedResource: null
                ),

                CreateResource(
                    "Example team",
                    "TEAM",
                    "TEAM.shared-identity"
                )
            };

        var entries = _normalizer.Normalize(resources);

        Assert.Equal(2, entries.Count);

        var group =
            Assert.Single(
                entries,
                entry => entry.ResourceName == "Example group"
            );

        Assert.Equal(
            BackupReportEntryClassification.Unknown,
            group.Classification
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GroupWithoutPopulatedTeamName_UsesExistingConsolidation(
        string? teamName)
    {
        var resources =
            new[]
            {
                CreateResource(
                    "Example group",
                    "GROUP",
                    "GROUP.shared-identity",
                    teamName: teamName,
                    status: "warning"
                ),

                CreateResource(
                    "Example team",
                    "TEAM",
                    "TEAM.shared-identity"
                )
            };

        var entry =
            Assert.Single(_normalizer.Normalize(resources));

        Assert.Equal("Example team", entry.ResourceName);

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification
        );

        Assert.Equal("warning", entry.LastResult);
    }

    private static AcronisMicrosoft365ResourceDto CreateResource(
        string name,
        string kind,
        string internalId,
        string? teamName = null,
        string status = "ok",
        bool? protectedResource = true)
    {
        return new AcronisMicrosoft365ResourceDto
        {
            Id = Guid.NewGuid().ToString(),
            InternalId = internalId,
            Name = name,
            Kind = kind,
            TeamName = teamName,
            HasProtections = protectedResource,
            LastTaskStatus = status,
            LastTaskState = "idle",
            LastSuccessTime = SuccessfulBackup,
            LastFinishTime = SuccessfulBackup
        };
    }
}
