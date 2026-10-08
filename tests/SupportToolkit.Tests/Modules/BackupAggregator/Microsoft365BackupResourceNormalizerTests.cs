using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class Microsoft365BackupResourceNormalizerTests
{
    private readonly Microsoft365BackupResourceNormalizer _normalizer =
        new();

    [Fact]
    public void Normalize_HealthyResource_IsHealthy()
    {
        var entry =
            NormalizeOne(
                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T12:00:00Z"
                        )
                )
            );

        Assert.Equal(
            BackupReportEntryClassification.Healthy,
            entry.Classification
        );

        Assert.Equal(
            "ok",
            entry.LastResult
        );
    }

    [Fact]
    public void Normalize_UnprotectedResource_RequiresReview()
    {
        var entry =
            NormalizeOne(
                CreateResource(
                    protectedResource: false,
                    status: "ok",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T12:00:00Z"
                        )
                )
            );

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification
        );

        Assert.Equal(
            "not_protected",
            entry.LastResult
        );
    }

    [Fact]
    public void Normalize_ConflictingProtectionSignals_RequiresReview()
    {
        var resource =
            CreateResource(
                protectedResource: true,
                status: "ok",
                state: "idle",
                lastSuccess:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    )
            );

        resource =
            new AcronisMicrosoft365ResourceDto
            {
                Id = resource.Id,
                Name = resource.Name,
                HasProtections = true,

                BasicKinds =
                [
                    new AcronisMicrosoft365BasicKindDto
                    {
                        Kind = "mailbox",
                        HasProtections = false
                    }
                ],

                LastTaskStatus =
                    resource.LastTaskStatus,

                LastTaskState =
                    resource.LastTaskState,

                LastSuccessTime =
                    resource.LastSuccessTime
            };

        var entry =
            NormalizeOne(
                resource
            );

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification
        );

        Assert.Equal(
            "protection_conflict",
            entry.LastResult
        );
    }

    [Fact]
    public void Normalize_ProtectedWithoutSuccessfulBackup_RequiresReview()
    {
        var entry =
            NormalizeOne(
                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: "idle",
                    lastSuccess:
                        null
                )
            );

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification
        );

        Assert.Equal(
            "no_successful_backup",
            entry.LastResult
        );
    }

    [Fact]
    public void Normalize_TaskFailure_PreservesVendorStatus()
    {
        var entry =
            NormalizeOne(
                CreateResource(
                    protectedResource: true,
                    status: "warning",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T12:00:00Z"
                        )
                )
            );

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification
        );

        Assert.Equal(
            "warning",
            entry.LastResult
        );
    }

    [Fact]
    public void Normalize_NonIdleTaskState_PreservesVendorState()
    {
        var entry =
            NormalizeOne(
                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: "running",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T12:00:00Z"
                        )
                )
            );

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification
        );

        Assert.Equal(
            "running",
            entry.LastResult
        );
    }

    [Fact]
    public void Normalize_MissingState_IsUnknown()
    {
        var entry =
            NormalizeOne(
                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: null,
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T12:00:00Z"
                        )
                )
            );

        Assert.Equal(
            BackupReportEntryClassification.Unknown,
            entry.Classification
        );
    }

    [Fact]
    public void Normalize_MissingName_IsUnknownAndPreserved()
    {
        var resource =
            CreateResource(
                protectedResource: true,
                status: "ok",
                state: "idle",
                lastSuccess:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    ),
                name:
                    null
            );

        var entry =
            NormalizeOne(
                resource
            );

        Assert.Equal(
            BackupReportEntryClassification.Unknown,
            entry.Classification
        );

        Assert.Equal(
            "<unknown Microsoft 365 resource>",
            entry.ResourceName
        );
    }

    private BackupReportEntry NormalizeOne(
        AcronisMicrosoft365ResourceDto resource)
    {
        return Assert.Single(
            _normalizer.Normalize(
                [resource]
            )
        );
    }

    private static AcronisMicrosoft365ResourceDto CreateResource(
        bool? protectedResource,
        string? status,
        string? state,
        DateTimeOffset? lastSuccess,
        string? name = "Mailbox")
    {
        return new AcronisMicrosoft365ResourceDto
        {
            Id = "resource-1",
            Name = name,
            HasProtections = protectedResource,
            LastTaskStatus = status,
            LastTaskState = state,
            LastSuccessTime = lastSuccess,
            LastFinishTime = lastSuccess
        };
    }
}