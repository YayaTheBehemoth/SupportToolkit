using SupportToolkit.Core.MonitoringScope;

using System.Collections.Concurrent;
using System.Net;
using SupportToolkit.Modules.BackupAggregator.Fixtures;
using SupportToolkit.Modules.BackupAggregator.MonitoringScope;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class ScopedBackupInventoryReviewTests
{
    private static readonly DateOnly EvaluationDate =
        new(2026, 10, 15);

    [Fact]
    public async Task ScopedReview_RequestsOnlySelectedTenants()
    {
        var selection =
            CreateFixtureSelection();

        var microsoft365 =
            new RecordingMicrosoft365Provider();

        var devices =
            new RecordingDeviceProvider();

        var service =
            CreateService(
                new ThrowingTenantProvider(),
                microsoft365,
                devices
            );

        var result =
            await service.ReviewSelectedTenantsAsync(
                selection
            );

        var expectedIds =
            selection.InventoryCandidates
                .Select(tenant => tenant.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            expectedIds,
            microsoft365.Requested
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray()
        );

        Assert.Equal(
            expectedIds,
            devices.Requested
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray()
        );

        Assert.Equal(3, result.Inventory.TenantsAttempted);
        Assert.Equal(3, result.Inventory.TenantCount);
        Assert.Equal(0, result.Inventory.FailedTenantCount);
        Assert.Equal(3, result.Inventory.RowsChecked);
        Assert.Equal(3, result.Inventory.HealthyRowsSuppressed);

        Assert.True(result.IsInventoryComplete);
        Assert.False(result.IsScopeComplete);
        Assert.False(result.IsOverallComplete);

        Assert.Equal(1, result.Selection.NeedsReviewCount);
        Assert.Equal(1, result.Selection.UnclassifiedCount);
    }

    [Fact]
    public async Task FullyResolvedScopeAndInventory_AreComplete()
    {
        var tenant =
            Tenant(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Customer A"
            );

        var selection =
            new MonitoringTenantSelectionResult(
                [
                    new MonitoringTenantSelectionFinding(
                        tenant,
                        MonitoringTenantSelectionStatus.CandidateForInventory,
                        "Synthetic verified monitoring evidence."
                    )
                ],
                []
            );

        var service =
            CreateService(
                new ThrowingTenantProvider(),
                new RecordingMicrosoft365Provider(),
                new RecordingDeviceProvider()
            );

        var result =
            await service.ReviewSelectedTenantsAsync(
                selection
            );

        Assert.True(result.IsInventoryComplete);
        Assert.True(result.IsScopeComplete);
        Assert.True(result.IsOverallComplete);
        Assert.Equal(1, result.Inventory.TenantCount);
    }

    [Fact]
    public async Task InventoryFailure_DoesNotBecomeScopeFailure()
    {
        var tenant =
            Tenant(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Customer A"
            );

        var selection =
            new MonitoringTenantSelectionResult(
                [
                    new MonitoringTenantSelectionFinding(
                        tenant,
                        MonitoringTenantSelectionStatus.CandidateForInventory,
                        "Synthetic active monitoring evidence."
                    )
                ],
                []
            );

        var microsoft365 =
            new RecordingMicrosoft365Provider(
                failingTenantId: tenant.Id
            );

        var devices =
            new RecordingDeviceProvider();

        var service =
            CreateService(
                new ThrowingTenantProvider(),
                microsoft365,
                devices
            );

        var result =
            await service.ReviewSelectedTenantsAsync(
                selection
            );

        Assert.True(result.IsScopeComplete);
        Assert.False(result.IsInventoryComplete);
        Assert.False(result.IsOverallComplete);

        Assert.Equal(1, result.Inventory.TenantsAttempted);
        Assert.Equal(1, result.Inventory.FailedTenantCount);
        Assert.Empty(result.Inventory.Tenants);

        Assert.Single(
            result.Inventory.Failures
        );

        Assert.Empty(
            devices.Requested
        );
    }

    [Fact]
    public async Task UnclassifiedScope_DoesNotRequestInventory()
    {
        var tenant =
            Tenant(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Customer A"
            );

        var selection =
            new MonitoringTenantSelectionResult(
                [
                    new MonitoringTenantSelectionFinding(
                        tenant,
                        MonitoringTenantSelectionStatus.Unclassified,
                        "No verified monitoring entitlement."
                    )
                ],
                []
            );

        var microsoft365 =
            new RecordingMicrosoft365Provider();

        var devices =
            new RecordingDeviceProvider();

        var service =
            CreateService(
                new ThrowingTenantProvider(),
                microsoft365,
                devices
            );

        var result =
            await service.ReviewSelectedTenantsAsync(
                selection
            );

        Assert.Empty(microsoft365.Requested);
        Assert.Empty(devices.Requested);

        Assert.Empty(result.Inventory.Tenants);
        Assert.Empty(result.Inventory.Failures);

        Assert.True(result.IsInventoryComplete);
        Assert.False(result.IsScopeComplete);
        Assert.False(result.IsOverallComplete);
    }

    [Fact]
    public async Task UnmatchedBusinessCentralEvidence_LeavesScopeIncomplete()
    {
        var tenant =
            Tenant(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Customer A"
            );

        var selection =
            new MonitoringTenantSelectionResult(
                [
                    new MonitoringTenantSelectionFinding(
                        tenant,
                        MonitoringTenantSelectionStatus.CandidateForInventory,
                        "Synthetic active monitoring evidence."
                    )
                ],
                [
                    new MonitoringEntitlementObservation(
                        "SUB-UNMATCHED",
                        "BC-UNMATCHED",
                        MonitoringEntitlementAssessment.ActiveCandidate,
                        "No resolved Acronis tenant."
                    )
                ]
            );

        var service =
            CreateService(
                new ThrowingTenantProvider(),
                new RecordingMicrosoft365Provider(),
                new RecordingDeviceProvider()
            );

        var result =
            await service.ReviewSelectedTenantsAsync(
                selection
            );

        Assert.True(result.IsInventoryComplete);
        Assert.False(result.IsScopeComplete);
        Assert.False(result.IsOverallComplete);

        Assert.Single(
            result.Selection.UnmatchedEvidence
        );
    }

    [Fact]
    public async Task DuplicateSelectedTenantIds_AreRejectedBeforeCollection()
    {
        var tenant =
            Tenant(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Customer A"
            );

        var duplicate =
            Tenant(
                tenant.Id,
                "Customer A duplicate"
            );

        var selection =
            new MonitoringTenantSelectionResult(
                [
                    new MonitoringTenantSelectionFinding(
                        tenant,
                        MonitoringTenantSelectionStatus.CandidateForInventory,
                        "First."
                    ),
                    new MonitoringTenantSelectionFinding(
                        duplicate,
                        MonitoringTenantSelectionStatus.CandidateForInventory,
                        "Second."
                    )
                ],
                []
            );

        var microsoft365 =
            new RecordingMicrosoft365Provider();

        var service =
            CreateService(
                new ThrowingTenantProvider(),
                microsoft365,
                new RecordingDeviceProvider()
            );

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ReviewSelectedTenantsAsync(
                selection
            )
        );

        Assert.Empty(microsoft365.Requested);
    }

    [Fact]
    public async Task UnscopedReview_StillReviewsEveryCustomerTenant()
    {
        var fixture =
            MonitoringTenantSelectionFixtureFactory.Create();

        var microsoft365 =
            new RecordingMicrosoft365Provider();

        var devices =
            new RecordingDeviceProvider();

        var service =
            CreateService(
                new RecordingTenantProvider(
                    fixture.Tenants
                ),
                microsoft365,
                devices
            );

        var report =
            await service.ReviewAllTenantsAsync();

        Assert.Equal(
            fixture.Tenants.Count,
            report.TenantsAttempted
        );

        Assert.Equal(
            fixture.Tenants.Count,
            report.TenantCount
        );

        Assert.Equal(
            fixture.Tenants.Count,
            microsoft365.Requested.Count
        );

        Assert.Equal(
            fixture.Tenants.Count,
            devices.Requested.Count
        );

        Assert.True(report.IsTenantReviewComplete);
    }

    private static MonitoringTenantSelectionResult
        CreateFixtureSelection()
    {
        var subscriptions =
            SmartSubscriptionFixtureFactory.Create();

        var acronis =
            MonitoringTenantSelectionFixtureFactory.Create();

        var observations =
            new SmartSubscriptionEntitlementEvaluator(
                SmartSubscriptionFixtureFactory.FixtureMonitoringItemNumber
            ).Evaluate(
                subscriptions.Headers,
                subscriptions.Lines,
                EvaluationDate
            );

        return new MonitoringTenantSelector()
            .Select(
                acronis.Tenants,
                observations,
                acronis.Identities
            );
    }

    private static BackupInventoryReviewService CreateService(
        IAcronisTenantProvider tenantProvider,
        IAcronisMicrosoft365InventoryProvider microsoft365,
        IAcronisDeviceInventoryProvider devices)
    {
        return new BackupInventoryReviewService(
            new AcronisTenantResolver(
                tenantProvider
            ),
            microsoft365,
            devices,
            new BackupInventoryNormalizer()
        );
    }

    private static TenantDto Tenant(
        string id,
        string name)
    {
        return new TenantDto
        {
            Id = id,
            Name = name,
            Kind = "customer"
        };
    }

    private sealed class ThrowingTenantProvider
        : IAcronisTenantProvider
    {
        public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Scoped review must not enumerate tenants."
            );
        }
    }

    private sealed class RecordingTenantProvider
        : IAcronisTenantProvider
    {
        private readonly IReadOnlyList<TenantDto> _tenants;

        public RecordingTenantProvider(
            IReadOnlyList<TenantDto> tenants)
        {
            _tenants = tenants;
        }

        public Task<IReadOnlyList<TenantDto>> GetTenantsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _tenants
            );
        }
    }

    private sealed class RecordingMicrosoft365Provider
        : IAcronisMicrosoft365InventoryProvider
    {
        private readonly string? _failingTenantId;

        public ConcurrentBag<string> Requested { get; } =
            new();

        public RecordingMicrosoft365Provider(
            string? failingTenantId = null)
        {
            _failingTenantId = failingTenantId;
        }

        public Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            Requested.Add(
                tenantId
            );

            if (string.Equals(
                tenantId,
                _failingTenantId,
                StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromException<
                    IReadOnlyList<AcronisMicrosoft365ResourceDto>>(
                    new HttpRequestException(
                        "Synthetic forbidden response.",
                        null,
                        HttpStatusCode.Forbidden
                    )
                );
            }

            return Task.FromResult<
                IReadOnlyList<AcronisMicrosoft365ResourceDto>>(
                []
            );
        }
    }

    private sealed class RecordingDeviceProvider
        : IAcronisDeviceInventoryProvider
    {
        public ConcurrentBag<string> Requested { get; } =
            new();

        public Task<IReadOnlyList<AcronisDeviceResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            Requested.Add(
                tenantId
            );

            var time =
                new DateTimeOffset(
                    2026, 10, 15,
                    12, 0, 0,
                    TimeSpan.Zero
                );

            IReadOnlyList<AcronisDeviceResourceDto> resources =
            [
                new AcronisDeviceResourceDto
                {
                    Id = "fixture-device",
                    Name = "Fixture Backup Device",
                    Status =
                        new AcronisDeviceResourceStatusDto
                        {
                            State = "ok",
                            LastBackup = time,
                            LastSuccessBackup = time,
                            AppliedPolicyNames = "Fixture Backup"
                        }
                }
            ];

            return Task.FromResult(resources);
        }
    }
}
