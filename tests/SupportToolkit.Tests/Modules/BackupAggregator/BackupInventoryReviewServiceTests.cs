using System.Net;
using SupportToolkit.Modules.BackupAggregator.Services;
using SupportToolkit.Providers.Acronis.Devices;
using SupportToolkit.Providers.Acronis.Devices.Dtos;
using SupportToolkit.Providers.Acronis.Microsoft365;
using SupportToolkit.Providers.Acronis.Microsoft365.Dtos;
using SupportToolkit.Providers.Acronis.Tenants;
using SupportToolkit.Providers.Acronis.Tenants.Dtos;

namespace SupportToolkit.Tests.Modules.BackupAggregator;

public class BackupInventoryReviewServiceTests
{
    [Fact]
    public async Task ReviewTenantAsync_CombinesBothInventoryDomains()
    {
        var microsoft365Resources =
            Enumerable.Range(
                    1,
                    41
                )
                .Select(
                    index =>
                        HealthyMicrosoft365(
                            $"m365-{index}",
                            $"M365 {index}"
                        )
                )
                .ToList();

        var deviceResources =
            new List<AcronisDeviceResourceDto>();

        for (var index = 1; index <= 3; index++)
        {
            deviceResources.Add(
                Device(
                    $"device-{index}",
                    $"SERVER-{index}",
                    "idle"
                )
            );
        }

        deviceResources.Add(
            Device(
                "device-4",
                "SQL-SRV",
                "notProtected"
            )
        );

        var tenant =
            Tenant(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Customer Alpha",
                "customer"
            );

        var service =
            CreateService(
                [tenant],
                new Dictionary<
                    string,
                    IReadOnlyList<AcronisMicrosoft365ResourceDto>>
                {
                    [tenant.Id] =
                        microsoft365Resources
                },
                new Dictionary<
                    string,
                    IReadOnlyList<AcronisDeviceResourceDto>>
                {
                    [tenant.Id] =
                        deviceResources
                }
            );

        var report =
            await service.ReviewTenantAsync(
                "Customer Alpha"
            );

        Assert.Equal(
            1,
            report.TenantsAttempted
        );

        Assert.Equal(
            1,
            report.TenantCount
        );

        Assert.Equal(
            0,
            report.FailedTenantCount
        );

        Assert.Equal(
            45,
            report.RowsChecked
        );

        Assert.Equal(
            44,
            report.HealthyRowsSuppressed
        );

        Assert.Equal(
            1,
            report.FindingsCount
        );

        Assert.Equal(
            0,
            report.UnknownRowsCount
        );

        Assert.True(
            report.IsFullyAccountedFor
        );

        Assert.True(
            report.IsTenantReviewComplete
        );
    }

    [Fact]
    public async Task ReviewAllTenantsAsync_ReviewsCustomerTenantsOnly()
    {
        var customerAlpha =
            Tenant(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Customer Alpha",
                "customer"
            );

        var partner =
            Tenant(
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                "Partner",
                "partner"
            );

        var customerBeta =
            Tenant(
                "cccccccc-cccc-cccc-cccc-cccccccccccc",
                "Customer Beta",
                "customer"
            );

        var microsoft365 =
            new Dictionary<
                string,
                IReadOnlyList<AcronisMicrosoft365ResourceDto>>
            {
                [customerAlpha.Id] =
                    [
                        HealthyMicrosoft365(
                            "m365-alpha",
                            "Alpha mailbox"
                        )
                    ],

                [customerBeta.Id] =
                    [
                        HealthyMicrosoft365(
                            "m365-beta",
                            "Beta mailbox"
                        )
                    ]
            };

        var devices =
            new Dictionary<
                string,
                IReadOnlyList<AcronisDeviceResourceDto>>
            {
                [customerAlpha.Id] =
                    [
                        Device(
                            "device-alpha",
                            "ALPHA-SRV",
                            "idle"
                        )
                    ],

                [customerBeta.Id] =
                    [
                        Device(
                            "device-beta",
                            "BETA-SRV",
                            "idle"
                        )
                    ]
            };

        var service =
            CreateService(
                [
                    partner,
                    customerBeta,
                    customerAlpha
                ],
                microsoft365,
                devices
            );

        var report =
            await service.ReviewAllTenantsAsync();

        Assert.Equal(
            2,
            report.TenantsAttempted
        );

        Assert.Equal(
            2,
            report.TenantCount
        );

        Assert.Equal(
            0,
            report.FailedTenantCount
        );

        Assert.Equal(
            4,
            report.RowsChecked
        );

        Assert.DoesNotContain(
            report.Tenants,
            tenant =>
                tenant.TenantName == "Partner"
        );
    }

    [Fact]
    public async Task ReviewAllTenantsAsync_OneTenantFailure_DoesNotAbortRemainingTenants()
    {
        var customerAlpha =
            Tenant(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Customer Alpha",
                "customer"
            );

        var customerBeta =
            Tenant(
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                "Customer Beta",
                "customer"
            );

        var microsoft365Provider =
            new StubMicrosoft365Provider(
                new Dictionary<
                    string,
                    IReadOnlyList<AcronisMicrosoft365ResourceDto>>
                {
                    [customerBeta.Id] =
                        [
                            HealthyMicrosoft365(
                                "m365-beta",
                                "Beta mailbox"
                            )
                        ]
                },
                new Dictionary<string, Exception>
                {
                    [customerAlpha.Id] =
                        new HttpRequestException(
                            "Forbidden",
                            null,
                            HttpStatusCode.Forbidden
                        )
                }
            );

        var deviceProvider =
            new StubDeviceProvider(
                new Dictionary<
                    string,
                    IReadOnlyList<AcronisDeviceResourceDto>>
                {
                    [customerBeta.Id] =
                        [
                            Device(
                                "device-beta",
                                "BETA-SRV",
                                "idle"
                            )
                        ]
                }
            );

        var service =
            new BackupInventoryReviewService(
                new AcronisTenantResolver(
                    new StubTenantProvider(
                        [
                            customerAlpha,
                            customerBeta
                        ]
                    )
                ),
                microsoft365Provider,
                deviceProvider,
                new BackupInventoryNormalizer()
            );

        var report =
            await service.ReviewAllTenantsAsync();

        Assert.Equal(
            2,
            report.TenantsAttempted
        );

        Assert.Equal(
            1,
            report.TenantCount
        );

        Assert.Equal(
            1,
            report.FailedTenantCount
        );

        Assert.False(
            report.IsTenantReviewComplete
        );

        var reviewedTenant =
            Assert.Single(
                report.Tenants
            );

        Assert.Equal(
            "Customer Beta",
            reviewedTenant.TenantName
        );

        var failure =
            Assert.Single(
                report.Failures
            );

        Assert.Equal(
            "Customer Alpha",
            failure.TenantName
        );

        Assert.Equal(
            "Microsoft 365 inventory",
            failure.Stage
        );

        Assert.Contains(
            "403",
            failure.Reason
        );
    }

    private static BackupInventoryReviewService CreateService(
        IReadOnlyList<TenantDto> tenants,
        IReadOnlyDictionary<
            string,
            IReadOnlyList<AcronisMicrosoft365ResourceDto>>
            microsoft365,
        IReadOnlyDictionary<
            string,
            IReadOnlyList<AcronisDeviceResourceDto>>
            devices)
    {
        return new BackupInventoryReviewService(
            new AcronisTenantResolver(
                new StubTenantProvider(
                    tenants
                )
            ),
            new StubMicrosoft365Provider(
                microsoft365
            ),
            new StubDeviceProvider(
                devices
            ),
            new BackupInventoryNormalizer()
        );
    }

    private static TenantDto Tenant(
        string id,
        string name,
        string kind)
    {
        return new TenantDto
        {
            Id = id,
            Name = name,
            Kind = kind
        };
    }

    private static AcronisMicrosoft365ResourceDto
        HealthyMicrosoft365(
            string id,
            string name)
    {
        return new AcronisMicrosoft365ResourceDto
        {
            Id = id,
            Name = name,
            HasProtections = true,
            LastTaskStatus = "ok",
            LastTaskState = "idle",
            LastSuccessTime =
                DateTimeOffset.Parse(
                    "2026-10-08T12:00:00Z"
                )
        };
    }

    private static AcronisDeviceResourceDto Device(
        string id,
        string name,
        string state)
    {
        var backupTime =
            DateTimeOffset.Parse(
                "2026-10-08T12:00:00Z"
            );

        return new AcronisDeviceResourceDto
        {
            Id = id,
            Name = name,

            Status =
                new AcronisDeviceResourceStatusDto
                {
                    State = state,
                    LastBackup = backupTime,
                    LastSuccessBackup = backupTime,

                    AppliedPolicyNames =
                        state == "notProtected"
                            ? "SQL Database Backup (Disabled)"
                            : "Server Backup"
                }
        };
    }

    private sealed class StubTenantProvider
        : IAcronisTenantProvider
    {
        private readonly IReadOnlyList<TenantDto> _tenants;

        public StubTenantProvider(
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

    private sealed class StubMicrosoft365Provider
        : IAcronisMicrosoft365InventoryProvider
    {
        private readonly IReadOnlyDictionary<
            string,
            IReadOnlyList<AcronisMicrosoft365ResourceDto>>
            _resources;

        private readonly IReadOnlyDictionary<
            string,
            Exception>
            _failures;

        public StubMicrosoft365Provider(
            IReadOnlyDictionary<
                string,
                IReadOnlyList<AcronisMicrosoft365ResourceDto>>
                resources,
            IReadOnlyDictionary<string, Exception>? failures = null)
        {
            _resources = resources;

            _failures =
                failures
                ?? new Dictionary<string, Exception>();
        }

        public Task<IReadOnlyList<AcronisMicrosoft365ResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            if (_failures.TryGetValue(
                    tenantId,
                    out var failure))
            {
                return Task.FromException<
                    IReadOnlyList<AcronisMicrosoft365ResourceDto>>(
                    failure
                );
            }

            return Task.FromResult(
                _resources.TryGetValue(
                    tenantId,
                    out var resources
                )
                    ? resources
                    : (IReadOnlyList<AcronisMicrosoft365ResourceDto>)[]
            );
        }
    }

    private sealed class StubDeviceProvider
        : IAcronisDeviceInventoryProvider
    {
        private readonly IReadOnlyDictionary<
            string,
            IReadOnlyList<AcronisDeviceResourceDto>>
            _resources;

        private readonly IReadOnlyDictionary<
            string,
            Exception>
            _failures;

        public StubDeviceProvider(
            IReadOnlyDictionary<
                string,
                IReadOnlyList<AcronisDeviceResourceDto>>
                resources,
            IReadOnlyDictionary<string, Exception>? failures = null)
        {
            _resources = resources;

            _failures =
                failures
                ?? new Dictionary<string, Exception>();
        }

        public Task<IReadOnlyList<AcronisDeviceResourceDto>>
            GetResourcesForTenantAsync(
                string tenantId,
                CancellationToken cancellationToken = default)
        {
            if (_failures.TryGetValue(
                    tenantId,
                    out var failure))
            {
                return Task.FromException<
                    IReadOnlyList<AcronisDeviceResourceDto>>(
                    failure
                );
            }

            return Task.FromResult(
                _resources.TryGetValue(
                    tenantId,
                    out var resources
                )
                    ? resources
                    : (IReadOnlyList<AcronisDeviceResourceDto>)[]
            );
        }
    }
}