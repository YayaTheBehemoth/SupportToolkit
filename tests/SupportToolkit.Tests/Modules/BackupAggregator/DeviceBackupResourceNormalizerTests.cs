using SupportToolkit.Modules.BackupAggregator.Models;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Devices.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class DeviceBackupResourceNormalizerTests
{
    private readonly DeviceBackupResourceNormalizer _normalizer =
        new();

    [Fact]
    public void Normalize_IdleWithSuccessfulBackup_IsHealthy()
    {
        var entry =
            NormalizeOne(
                state: "idle",
                lastBackup:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    ),
                lastSuccess:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    )
            );

        Assert.Equal(
            BackupReportEntryClassification.Healthy,
            entry.Classification
        );
    }

    [Theory]
    [InlineData("notProtected")]
    [InlineData("not_protected")]
    [InlineData("not_run")]
    public void Normalize_NotProtectedStates_RequireReview(
        string state)
    {
        var entry =
            NormalizeOne(
                state,
                DateTimeOffset.Parse(
                    "2026-10-08T12:00:00Z"
                ),
                DateTimeOffset.Parse(
                    "2026-10-08T12:00:00Z"
                )
            );

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification
        );
    }

    [Fact]
    public void Normalize_Running_RemainsVisibleForNow()
    {
        var entry =
            NormalizeOne(
                state: "running",
                lastBackup:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    ),
                lastSuccess:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    )
            );

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification
        );
    }

    [Fact]
    public void Normalize_ScheduledWithoutPreviousBackup_RequiresReview()
    {
        var resource =
            new AcronisDeviceResourceDto
            {
                Id = "device-1",
                Name = "SERVER-01",
                Status =
                    new AcronisDeviceResourceStatusDto
                    {
                        State = "idle",
                        LastBackup = null,
                        LastSuccessBackup = null,
                        NextBackup =
                            DateTimeOffset.Parse(
                                "2026-10-09T12:00:00Z"
                            )
                    }
            };

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
    public void Normalize_IdleWithoutSuccessfulBackup_IsUnknown()
    {
        var entry =
            NormalizeOne(
                state: "idle",
                lastBackup:
                    DateTimeOffset.Parse(
                        "2026-10-08T12:00:00Z"
                    ),
                lastSuccess: null
            );

        Assert.Equal(
            BackupReportEntryClassification.Unknown,
            entry.Classification
        );
    }

    [Fact]
    public void Normalize_UnknownVendorState_RemainsVisible()
    {
        var entry =
            NormalizeOne(
                state: "future_vendor_state",
                lastBackup: null,
                lastSuccess: null
            );

        Assert.Equal(
            BackupReportEntryClassification.NeedsReview,
            entry.Classification
        );
    }

    private BackupReportEntry NormalizeOne(
        string state,
        DateTimeOffset? lastBackup,
        DateTimeOffset? lastSuccess)
    {
        return Assert.Single(
            _normalizer.Normalize(
                [
                    new AcronisDeviceResourceDto
                    {
                        Id = "device-1",
                        Name = "SERVER-01",
                        Status =
                            new AcronisDeviceResourceStatusDto
                            {
                                State = state,
                                LastBackup = lastBackup,
                                LastSuccessBackup = lastSuccess
                            }
                    }
                ]
            )
        );
    }
}
