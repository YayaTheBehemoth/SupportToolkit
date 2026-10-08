# SupportToolkit

SupportToolkit is a modular internal-support automation toolkit.

The project currently contains two Acronis-focused modules:

- BackupHealth — exception-based health checks over the broader Acronis resource and alert surfaces.
- BackupAggregator — current backup-inventory review across Microsoft 365 and device workloads.

The goal is to replace repetitive portal and PDF review with conservative, read-only automation that still preserves human review of anything uncertain or unhealthy.

## BackupAggregator

BackupAggregator reviews the two Acronis inventory domains that together represent the current backup workload inventory:

    Acronis tenant
    ├── Microsoft 365 inventory
    │   └── /bc/api/resource_manager/v1/o365/...
    └── Device inventory
        └── /bc/api/resource_manager/v1/epm/resources
            using Acronis' built-in AllMachines group

Healthy resources are counted but suppressed from detailed output. Resources that require attention and resources that cannot be classified safely remain visible.

The accounting invariant is deliberate:

    checked = healthy + requires attention + unclassified

If that invariant is ever broken, detailed reporting stops rather than presenting an incomplete result as trustworthy.

### BackupAggregator architecture

    BackupAggregatorModule
            |
            v
    BackupAggregatorProductionSession
            |
            +--> AcronisTenantResolver
            |
            +--> AcronisMicrosoft365InventoryProvider
            |
            +--> AcronisDeviceInventoryProvider
            |
            v
    BackupInventoryReviewService
            |
            v
    BackupInventoryNormalizer
            |
            +--> Microsoft365BackupResourceNormalizer
            +--> DeviceBackupResourceNormalizer
            |
            v
    TenantBackupReport / AggregatedBackupReport
            |
            v
    ConsoleBackupAggregatorReporter

Provider names intentionally describe the operational inventory they expose rather than Acronis-internal abbreviations.

## BackupHealth

BackupHealth remains the broader exception-evaluation module. It correlates tenant, resource-status, and alert data through the shared Acronis provider infrastructure.

## Read-only Acronis boundary

All production Acronis traffic passes through AcronisReadOnlyHandler.

The handler permits only explicitly approved GET endpoints plus the OAuth token POST required for authentication. Unexpected hosts, protocols, methods, request bodies, and endpoints are rejected before transmission.

Credentials are supplied only at runtime:

    ACRONIS_DATACENTER_URL
    ACRONIS_CLIENT_ID
    ACRONIS_CLIENT_SECRET

Never commit credentials or production payloads.

## Running locally

Requirements:

- .NET 10 SDK

Run all tests:

    dotnet test

Run the application:

    dotnet run --project src\SupportToolkit

### Production mode

    set SUPPORTTOOLKIT_MODE=production

Review one tenant:

    dotnet run --project src\SupportToolkit -- backup-aggregator inventory-review "TENANT NAME"

### Fixture mode

Fixture mode does not contact Acronis.

    set SUPPORTTOOLKIT_MODE=fixture
    dotnet run --project src\SupportToolkit -- backup-aggregator

## Logging

Normal BackupAggregator runs keep INFO logging intentionally small:

    Starting backup inventory review.
    Tenant resolved.
    Microsoft 365 resources: ...
    Device resources: ...
    Backup inventory review complete: ...

Transport, authentication, pagination, and provider diagnostics are only wired into BackupAggregator when debug mode is enabled:

    set SUPPORTTOOLKIT_DEBUG=true

Disable it again with:

    set SUPPORTTOOLKIT_DEBUG=

## Testing

BackupAggregator tests cover:

- Microsoft 365 healthy, unhealthy, and unknown classification
- device idle, notProtected, scheduled, and unknown-state handling
- duplicate-resource accounting
- preservation of identity-less resources
- exact tenant resolution and ambiguity handling
- aggregation accounting invariants
- combined Microsoft 365 plus device inventory regression behavior
- console suppression of healthy detail rows
- Microsoft 365 leaf-group discovery and pagination
- canonical Acronis AllMachines device inventory routing and pagination
- read-only transport allowlisting

Run:

    dotnet test

## Next milestone

The next BackupAggregator milestone is an all-tenants review built on the single-tenant inventory-review service.

The single-tenant path should remain the behavioral baseline: each tenant is independently resolved, both inventory domains are fetched, every normalized resource is accounted for, and only exceptions are expanded in detailed output.
