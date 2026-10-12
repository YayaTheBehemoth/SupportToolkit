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

    [Fact]
    public void Normalize_TeamAndBackingGroup_BecomeOneLogicalWorkload()
    {
        var resources =
            new[]
            {
                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T12:00:00Z"
                        ),
                    name:
                        "Deployment team",
                    kind:
                        "GROUP",
                    internalId:
                        "GROUP.c65366dd-00d2-35e9-9dc1-1aad8254f1be"
                ),

                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T13:00:00Z"
                        ),
                    name:
                        "Deployment team",
                    kind:
                        "TEAM",
                    internalId:
                        "TEAM.c65366dd-00d2-35e9-9dc1-1aad8254f1be"
                )
            };

        var entry =
            Assert.Single(
                _normalizer.Normalize(
                    resources
                )
            );

        Assert.Equal(
            "Deployment team",
            entry.ResourceName
        );

        Assert.Equal(
            BackupReportEntryClassification.Healthy,
            entry.Classification
        );
    }

    [Fact]
    public void Normalize_TeamBackingGroupFailure_IsNotSuppressed()
    {
        var resources =
            new[]
            {
                CreateResource(
                    protectedResource: true,
                    status: "warning",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T12:00:00Z"
                        ),
                    name:
                        "LinkedIN",
                    kind:
                        "GROUP",
                    internalId:
                        "GROUP.49a72b1c-44fb-3621-8377-18ba772216eb"
                ),

                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T13:00:00Z"
                        ),
                    name:
                        "LinkedIN",
                    kind:
                        "TEAM",
                    internalId:
                        "TEAM.49a72b1c-44fb-3621-8377-18ba772216eb"
                )
            };

        var entry =
            Assert.Single(
                _normalizer.Normalize(
                    resources
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

        Assert.Equal(
            "LinkedIN",
            entry.ResourceName
        );
    }

    [Fact]
    public void Normalize_UnrelatedGroup_IsPreserved()
    {
        var resources =
            new[]
            {
                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T12:00:00Z"
                        ),
                    name:
                        "Finance",
                    kind:
                        "GROUP",
                    internalId:
                        "GROUP.11111111-1111-1111-1111-111111111111"
                ),

                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T13:00:00Z"
                        ),
                    name:
                        "Tools",
                    kind:
                        "TEAM",
                    internalId:
                        "TEAM.22222222-2222-2222-2222-222222222222"
                )
            };

        var entries =
            _normalizer.Normalize(
                resources
            );

        Assert.Equal(
            2,
            entries.Count
        );

        Assert.Contains(
            entries,
            entry =>
                entry.ResourceName
                == "Finance"
        );

        Assert.Contains(
            entries,
            entry =>
                entry.ResourceName
                == "Tools"
        );
    }

    [Fact]
    public void Normalize_TeamWithoutUsableInternalId_DoesNotCollapseGroup()
    {
        var resources =
            new[]
            {
                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T12:00:00Z"
                        ),
                    name:
                        "Same name",
                    kind:
                        "GROUP",
                    internalId:
                        null
                ),

                CreateResource(
                    protectedResource: true,
                    status: "ok",
                    state: "idle",
                    lastSuccess:
                        DateTimeOffset.Parse(
                            "2026-10-08T13:00:00Z"
                        ),
                    name:
                        "Same name",
                    kind:
                        "TEAM",
                    internalId:
                        null
                )
            };

        var entries =
            _normalizer.Normalize(
                resources
            );

        Assert.Equal(
            2,
            entries.Count
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
        string? name = "Mailbox",
        string? kind = "USER",
        string? internalId = "USER.resource-1")
    {
        return new AcronisMicrosoft365ResourceDto
        {
            Id =
                Guid.NewGuid()
                    .ToString(),

            InternalId =
                internalId,

            Name =
                name,

            Kind =
                kind,

            HasProtections =
                protectedResource,

            LastTaskStatus =
                status,

            LastTaskState =
                state,

            LastSuccessTime =
                lastSuccess,

            LastFinishTime =
                lastSuccess
        };
    }
}