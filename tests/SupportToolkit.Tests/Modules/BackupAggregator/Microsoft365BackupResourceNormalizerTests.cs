using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class Microsoft365BackupResourceNormalizerTests
{
    private readonly Microsoft365BackupResourceNormalizer _normalizer =
        new();

    [Fact]
    public void Normalize_HealthyResource_IsSuppressedAsHealthy()
    {
        var resource =
            CreateResource(
                id: "resource-1",
                name: "Mailbox",
                protectedResource: true,
                status: "ok",
                state: "idle",
                lastSuccess:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    )
            );

        var entry =
            Assert.Single(
                _normalizer.Normalize([resource])
            );

        Assert.Equal(
            BackupReportEntryClassification.Healthy,
            entry.Classification
        );
    }

    [Fact]
    public void Normalize_UnprotectedResource_RequiresReview()
    {
        var resource =
            CreateResource(
                id: "resource-1",
                name: "Mailbox",
                protectedResource: false,
                status: "ok",
                state: "idle",
                lastSuccess:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    )
            );

        var entry =
            Assert.Single(
                _normalizer.Normalize([resource])
            );

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification
        );
    }

    [Fact]
    public void Normalize_MissingState_IsUnknown()
    {
        var resource =
            CreateResource(
                id: "resource-1",
                name: "Mailbox",
                protectedResource: true,
                status: "ok",
                state: null,
                lastSuccess:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    )
            );

        var entry =
            Assert.Single(
                _normalizer.Normalize([resource])
            );

        Assert.Equal(
            BackupReportEntryClassification.Unknown,
            entry.Classification
        );
    }

    [Fact]
    public void Normalize_DuplicateResourceIdentity_IsCountedOnce()
    {
        var first =
            CreateResource(
                "resource-1",
                "Mailbox",
                true,
                "ok",
                "idle",
                DateTimeOffset.Parse(
                    "2026-10-08T12:00:00Z"
                )
            );

        var duplicate =
            CreateResource(
                "resource-1",
                "Mailbox",
                true,
                "ok",
                "idle",
                DateTimeOffset.Parse(
                    "2026-10-08T12:00:00Z"
                )
            );

        var entries =
            _normalizer.Normalize(
                [first, duplicate]
            );

        Assert.Single(entries);
    }

    [Fact]
    public void Normalize_ResourceWithoutIdentity_IsNotDiscarded()
    {
        var resource =
            CreateResource(
                id: null,
                name: "Mailbox",
                protectedResource: true,
                status: "ok",
                state: "idle",
                lastSuccess:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    )
            );

        var entries =
            _normalizer.Normalize(
                [resource]
            );

        Assert.Single(entries);
    }

    private static AcronisMicrosoft365ResourceDto CreateResource(
        string? id,
        string? name,
        bool? protectedResource,
        string? status,
        string? state,
        DateTimeOffset? lastSuccess)
    {
        return new AcronisMicrosoft365ResourceDto
        {
            Id = id,
            Name = name,
            HasProtections = protectedResource,
            LastTaskStatus = status,
            LastTaskState = state,
            LastSuccessTime = lastSuccess,
            LastFinishTime = lastSuccess
        };
    }
}
