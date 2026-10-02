using System.Text.Json;
using SupportToolkit.Modules.BackupHealth;
using SupportToolkit.Modules.BackupHealth.Models;
using SupportToolkit.Providers.Acronis;
using SupportToolkit.Providers.Acronis.Dtos;

namespace SupportToolkit.Tests;

public class BackupHealthServiceTests
{
    private static BackupHealthService CreateService()
    {
        var fixtureDirectory = Path.Combine(
            "Fixtures",
            "Acronis"
        );

        var provider =
            new FixtureAcronisProvider(
                fixtureDirectory
            );

        return new BackupHealthService(provider);
    }

    [Fact]
    public async Task GetSnapshotAsync_JoinsResourcesWithTenants()
    {
        var service = CreateService();

        var resources =
            (await service.GetSnapshotAsync())
            .Resources;

        Assert.Equal(
            4,
            resources.Count
        );

        var resource =
            resources.Single(
                resource =>
                    resource.ResourceName
                    == "BACKUP-SERVER-01"
            );

        Assert.Equal(
            "Customer Alpha",
            resource.TenantName
        );

        Assert.Equal(
            "idle",
            resource.Status
        );

        Assert.NotNull(
            resource.LastSuccessfulBackup
        );
    }

    [Fact]
    public async Task GetSnapshotAsync_MapsFailedResource()
    {
        var service = CreateService();

        var resources =
            (await service.GetSnapshotAsync())
            .Resources;

        var resource =
            resources.Single(
                resource =>
                    resource.ResourceName
                    == "FILES-01"
            );

        Assert.Equal(
            "Customer Beta",
            resource.TenantName
        );

        Assert.Equal(
            "error",
            resource.Status
        );

        Assert.Equal(
            new DateTimeOffset(
                2026,
                9,
                27,
                3,
                0,
                0,
                TimeSpan.Zero
            ),
            resource.LastSuccessfulBackup
        );
    }

    [Fact]
    public async Task GetSnapshotAsync_AttachesAlertsToMatchingResource()
    {
        var service = CreateService();

        var resources =
            (await service.GetSnapshotAsync())
            .Resources;

        var resource =
            resources.Single(
                resource =>
                    resource.ResourceName
                    == "FILES-01"
            );

        Assert.Single(
            resource.Alerts
        );

        var alert =
            resource.Alerts[0];

        Assert.Equal(
            "BackupFailed",
            alert.Type
        );

        Assert.Equal(
            "critical",
            alert.Severity
        );
    }

    [Fact]
    public async Task GetSnapshotAsync_ResourceWithoutAlertsHasEmptyAlertList()
    {
        var service = CreateService();

        var resources =
            (await service.GetSnapshotAsync())
            .Resources;

        var resource =
            resources.Single(
                resource =>
                    resource.ResourceName
                    == "APP-SERVER-01"
            );

        Assert.Empty(
            resource.Alerts
        );
    }

    [Fact]
    public async Task GetSnapshotAsync_MapsHealthyResource()
    {
        var service = CreateService();

        var resources =
            (await service.GetSnapshotAsync())
            .Resources;

        var resource =
            resources.Single(
                resource =>
                    resource.ResourceName
                    == "APP-SERVER-01"
            );

        Assert.Equal(
            "Customer Alpha",
            resource.TenantName
        );

        Assert.Equal(
            "idle",
            resource.Status
        );

        Assert.Equal(
            new DateTimeOffset(
                2026,
                10,
                1,
                4,
                0,
                0,
                TimeSpan.Zero
            ),
            resource.LastSuccessfulBackup
        );

        Assert.Empty(
            resource.Alerts
        );
    }

    [Fact]
    public async Task GetSnapshotAsync_ResourceMissingId_IsSkippedAndReported()
    {
        var provider =
            new StubAcronisProvider
            {
                Tenants =
                [
                    CreateTenant(
                        "tenant-001",
                        "Customer Alpha"
                    )
                ],

                ResourceStatuses =
                [
                    CreateResourceStatus(
                        id: null,
                        tenantId: "tenant-001",
                        name: "NO-ID-SERVER"
                    )
                ]
            };

        var service =
            new BackupHealthService(provider);

        var snapshot =
            await service.GetSnapshotAsync();

        Assert.Empty(
            snapshot.Resources
        );

        var diagnostic =
            Assert.Single(
                snapshot.Diagnostics
            );

        Assert.Equal(
            BackupHealthDiagnosticKind.ResourceMissingId,
            diagnostic.Kind
        );

        Assert.Contains(
            "NO-ID-SERVER",
            diagnostic.Message
        );
    }

    [Fact]
    public async Task GetSnapshotAsync_ResourceMissingTenantId_IsSkippedAndReported()
    {
        var provider =
            new StubAcronisProvider
            {
                Tenants =
                [
                    CreateTenant(
                        "tenant-001",
                        "Customer Alpha"
                    )
                ],

                ResourceStatuses =
                [
                    CreateResourceStatus(
                        id: "resource-001",
                        tenantId: null,
                        name: "NO-TENANT-SERVER"
                    )
                ]
            };

        var service =
            new BackupHealthService(provider);

        var snapshot =
            await service.GetSnapshotAsync();

        Assert.Empty(
            snapshot.Resources
        );

        var diagnostic =
            Assert.Single(
                snapshot.Diagnostics
            );

        Assert.Equal(
            BackupHealthDiagnosticKind.ResourceMissingTenantId,
            diagnostic.Kind
        );

        Assert.Contains(
            "NO-TENANT-SERVER",
            diagnostic.Message
        );
    }

    [Fact]
    public async Task GetSnapshotAsync_UnknownTenant_KeepsResourceAndReportsDiagnostic()
    {
        var provider =
            new StubAcronisProvider
            {
                ResourceStatuses =
                [
                    CreateResourceStatus(
                        id: "resource-001",
                        tenantId: "unknown-tenant",
                        name: "ORPHANED-SERVER"
                    )
                ]
            };

        var service =
            new BackupHealthService(provider);

        var snapshot =
            await service.GetSnapshotAsync();

        var resource =
            Assert.Single(
                snapshot.Resources
            );

        Assert.Equal(
            "Unknown tenant",
            resource.TenantName
        );

        var diagnostic =
            Assert.Single(
                snapshot.Diagnostics
            );

        Assert.Equal(
            BackupHealthDiagnosticKind.ResourceUnknownTenant,
            diagnostic.Kind
        );

        Assert.Contains(
            "unknown-tenant",
            diagnostic.Message
        );
    }

    [Fact]
    public async Task GetSnapshotAsync_AlertMissingResourceId_IsReported()
    {
        var provider =
            new StubAcronisProvider
            {
                Tenants =
                [
                    CreateTenant(
                        "tenant-001",
                        "Customer Alpha"
                    )
                ],

                ResourceStatuses =
                [
                    CreateResourceStatus(
                        id: "resource-001",
                        tenantId: "tenant-001",
                        name: "SERVER-01"
                    )
                ],

                Alerts =
                [
                    CreateAlert(
                        id: "alert-001",
                        detailsJson: "{}"
                    )
                ]
            };

        var service =
            new BackupHealthService(provider);

        var snapshot =
            await service.GetSnapshotAsync();

        var resource =
            Assert.Single(
                snapshot.Resources
            );

        Assert.Empty(
            resource.Alerts
        );

        var diagnostic =
            Assert.Single(
                snapshot.Diagnostics
            );

        Assert.Equal(
            BackupHealthDiagnosticKind.AlertMissingResourceId,
            diagnostic.Kind
        );

        Assert.Contains(
            "alert-001",
            diagnostic.Message
        );
    }

    [Fact]
    public async Task GetSnapshotAsync_AlertForUnknownResource_IsReported()
    {
        var provider =
            new StubAcronisProvider
            {
                Tenants =
                [
                    CreateTenant(
                        "tenant-001",
                        "Customer Alpha"
                    )
                ],

                ResourceStatuses =
                [
                    CreateResourceStatus(
                        id: "resource-001",
                        tenantId: "tenant-001",
                        name: "SERVER-01"
                    )
                ],

                Alerts =
                [
                    CreateAlert(
                        id: "alert-001",
                        detailsJson:
                            """
                            {
                              "resourceId": "resource-does-not-exist"
                            }
                            """
                    )
                ]
            };

        var service =
            new BackupHealthService(provider);

        var snapshot =
            await service.GetSnapshotAsync();

        var resource =
            Assert.Single(
                snapshot.Resources
            );

        Assert.Empty(
            resource.Alerts
        );

        var diagnostic =
            Assert.Single(
                snapshot.Diagnostics
            );

        Assert.Equal(
            BackupHealthDiagnosticKind.AlertUnmatchedResource,
            diagnostic.Kind
        );

        Assert.Contains(
            "resource-does-not-exist",
            diagnostic.Message
        );
    }

    private static TenantDto CreateTenant(
        string id,
        string name)
    {
        return new TenantDto
        {
            Id = id,
            Name = name
        };
    }

    private static ResourceStatusDto CreateResourceStatus(
        string? id,
        string? tenantId,
        string name)
    {
        return new ResourceStatusDto
        {
            Context =
                new ResourceDto
                {
                    Id = id,
                    TenantId = tenantId,
                    Name = name,
                    Type = "resource.machine"
                },

            Aggregate =
                new AggregateStatusDto
                {
                    Status = "idle"
                },

            Policies = []
        };
    }

    private static AlertDto CreateAlert(
        string id,
        string detailsJson)
    {
        return new AlertDto
        {
            Id = id,

            CreatedAt =
                new DateTimeOffset(
                    2026,
                    10,
                    1,
                    12,
                    0,
                    0,
                    TimeSpan.Zero
                ),

            Category = "Backup",
            Severity = "warning",
            Type = "FixtureAlert",

            Details =
                ParseJsonElement(
                    detailsJson
                )
        };
    }

    private static JsonElement ParseJsonElement(
        string json)
    {
        using var document =
            JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    private sealed class StubAcronisProvider
        : IAcronisProvider
    {
        public IReadOnlyList<ResourceStatusDto>
            ResourceStatuses
        { get; init; }
            = [];

        public IReadOnlyList<TenantDto>
            Tenants
        { get; init; }
            = [];

        public IReadOnlyList<AlertDto>
            Alerts
        { get; init; }
            = [];

        public Task<IReadOnlyList<ResourceStatusDto>>
            GetResourceStatusesAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                ResourceStatuses
            );
        }

        public Task<IReadOnlyList<TenantDto>>
            GetTenantsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Tenants
            );
        }

        public Task<IReadOnlyList<AlertDto>>
            GetAlertsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Alerts
            );
        }
    }
}