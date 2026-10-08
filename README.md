# SupportToolkit

SupportToolkit is a modular internal-support automation toolkit focused on reducing repetitive MSP support work through conservative, read-only automation.

The project currently focuses on Acronis backup review.

## Current status

### BackupAggregator

**BackupAggregator is the currently usable module.**

It reviews Acronis backup inventory, suppresses resources that can be classified as healthy, and surfaces only resources that require attention or cannot be classified safely.

The current implementation has been validated against the Acronis inventory surfaces used in the existing support workflow:

- Microsoft 365 backup resources
- The Acronis **All devices** inventory

These two sources currently provide the backup resources required by the validated support workflow.

This does **not** mean that BackupAggregator has proven exhaustive coverage of every workload type supported by Acronis.

Acronis exposes additional workload-specific views, and the available categories can differ between tenants. Examples include Hyper-V, Microsoft SQL, machines with agents, and other workload-specific sections.

Some of these views may represent resources already present in **All devices**, while others may become relevant for tenants or workloads not yet encountered.

BackupAggregator therefore treats its current coverage as:

> **Validated coverage for the backup inventory currently used in the support workflow.**

Additional workload types should be explicitly validated before they are considered safely covered.

### BackupHealth

**BackupHealth is experimental and still in progress.**

It was created while exploring broader Acronis resource-status and alert APIs and currently serves primarily as development groundwork for possible future health-check functionality.

BackupHealth is **not currently considered an operational backup-review workflow** and should not be treated as equivalent in maturity to BackupAggregator.

A future module may replace or supersede parts of BackupHealth as the toolkit evolves.

---

# BackupAggregator

BackupAggregator performs an exception-based review of Acronis backup inventory.

Instead of manually reviewing every healthy workload, the tool:

1. fetches the current backup inventory,
2. normalizes the different Acronis resource types,
3. classifies each resource,
4. counts healthy resources,
5. suppresses healthy resources from detailed output,
6. surfaces resources requiring attention,
7. surfaces resources that cannot be classified safely.

Example:

```text
SUPPORTTOOLKIT // BACKUP REVIEW
===============================

SUMMARY
-------

Tenants checked               1
Tenants requiring attention   1
Backup resources checked      45
Healthy                       44
Require attention             1
Unclassified                  0
Coverage                      45/45

ATTENTION REQUIRED
------------------

Customer Example

Resource                       Status                    Plan                        Last activity (UTC)
------------------------------ ------------------------  --------------------------  -------------------
SQL-SRV                        Not protected             SQL Database Backup (...)   2026-10-08 16:00

44 healthy backup resources were checked and intentionally suppressed from details.
```

The intention is that an operator can immediately answer:

```text
How many backup resources were checked?
How many are healthy?
Is anything wrong?
What specifically requires attention?
```

without manually reading through a report full of healthy rows.

---

## Current inventory coverage

BackupAggregator currently reads two Acronis inventory domains.

```text
Acronis tenant
├── Microsoft 365 inventory
│   └── /bc/api/resource_manager/v1/o365/...
│
└── Device inventory
    └── /bc/api/resource_manager/v1/epm/resources
        using Acronis' built-in AllMachines group
```

### Microsoft 365

Microsoft 365 resources are discovered through Acronis Resource Manager groups and resource endpoints.

The provider:

- discovers queryable Microsoft 365 leaf groups,
- follows cursor pagination,
- combines resources from those groups,
- removes duplicate resource identities,
- preserves resources without usable identities so they cannot disappear silently.

### Device inventory

Device workloads are retrieved from the Acronis **All devices** inventory.

Acronis internally exposes this through its built-in `AllMachines` virtual group.

The resulting inventory can contain different resource types such as:

- machines,
- Hyper-V virtual machines,
- SQL-related machine resources,
- other endpoint/device workload types exposed by Acronis.

### Coverage limitation

BackupAggregator does **not currently claim** that the two inventory sources above cover every workload category that Acronis can possibly expose.

The Acronis interface can contain additional workload-specific sections, and those sections can vary between tenants.

Those workload categories should be investigated and validated as they are encountered.

The system should prefer:

```text
visible uncertainty
```

over:

```text
silent assumptions
```

---

# Classification

BackupAggregator uses conservative classification.

A resource is only suppressed when the available Acronis data provides sufficient evidence that it is healthy.

Current classifications are:

```text
Healthy
NeedsReview
Unknown
```

## Healthy

Healthy resources are counted but omitted from detailed output.

Examples include:

```text
Microsoft 365:
lastTaskStatus = ok
lastTaskState  = idle
successful backup exists

Device:
status.state = idle
successful backup exists
```

Acronis' own frontend maps the device state `idle` to its green **OK** status under the normal completed-backup case.

## NeedsReview

Resources remain visible when something requires human attention.

Examples include:

```text
notProtected
not_protected
critical
error
warning
paused
interaction required
conflicting protection signals
unknown vendor states
```

Unknown Acronis states are intentionally surfaced instead of being assumed healthy.

## Unknown

`Unknown` is used when the payload does not contain enough information for a safe classification.

Examples include:

- missing resource state,
- missing resource identity/name,
- missing protection information,
- an idle resource with no successful backup evidence.

Unknown resources are counted and displayed.

---

# Inventory accounting

BackupAggregator maintains an explicit accounting invariant:

```text
Resources checked
=
Healthy
+ NeedsReview
+ Unknown
```

The report exposes this as:

```text
Coverage 45/45
```

Every normalized resource must be accounted for.

If this invariant fails, detailed output is stopped rather than presenting an incomplete result as trustworthy.

This is intentional.

---

# Architecture

BackupAggregator separates Acronis transport concerns from backup-review logic.

```text
BackupAggregatorModule
        |
        v
BackupAggregatorProductionSession
        |
        +--> AcronisTenantProvider
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
        |
        +--> DeviceBackupResourceNormalizer
        |
        v
TenantBackupReport
        |
        v
AggregatedBackupReport
        |
        v
ConsoleBackupAggregatorReporter
```

Provider names describe the operational inventory they expose rather than requiring callers to understand Acronis-internal terminology.

---

# Acronis provider architecture

The Acronis integration is split into capability-focused providers.

Examples include:

```text
AcronisTenantProvider
AcronisMicrosoft365InventoryProvider
AcronisDeviceInventoryProvider
AcronisResourceStatusProvider
AcronisAlertProvider
```

Shared transport functionality includes:

```text
AcronisApiClient
AcronisCursorPaginator
AcronisReadOnlyHandler
```

`HttpAcronisProvider` remains as a compatibility facade for older code such as BackupHealth.

New functionality should prefer the narrower capability-specific providers.

---

# Read-only safety boundary

Production Acronis traffic passes through:

```text
AcronisReadOnlyHandler
```

The handler permits only explicitly approved read endpoints plus the OAuth token request required for authentication.

It rejects:

- unexpected HTTP methods,
- unapproved endpoints,
- unexpected hosts,
- non-HTTPS traffic,
- GET requests containing request bodies.

The intent is that SupportToolkit should not be capable of modifying customer Acronis environments through the current integration.

---

# Running locally

## Requirements

- .NET 10 SDK

Clone the repository and open a terminal in the repository root.

Build:

```cmd
dotnet build
```

Run tests:

```cmd
dotnet test
```

---

# Production mode

Production mode must be explicitly enabled.

```cmd
set SUPPORTTOOLKIT_MODE=production
```

The following environment variables are required:

```text
ACRONIS_DATACENTER_URL
ACRONIS_CLIENT_ID
ACRONIS_CLIENT_SECRET
```

Example:

```cmd
set ACRONIS_DATACENTER_URL=https://example-cloud.acronis.com
set ACRONIS_CLIENT_ID=...
set ACRONIS_CLIENT_SECRET=...
```

Do not commit credentials to the repository.

## Review one tenant

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator inventory-review "TENANT NAME"
```

Example:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator inventory-review "Customer Example"
```

---

# Fixture mode

Fixture mode can be used without contacting Acronis.

```cmd
set SUPPORTTOOLKIT_MODE=fixture
```

Run:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator
```

Fixture mode exists for deterministic development, testing, and demonstration.

To return to production mode:

```cmd
set SUPPORTTOOLKIT_MODE=production
```

---

# Debug logging

Normal BackupAggregator runs intentionally keep logging compact.

Example:

```text
Starting backup inventory review.
Tenant resolved.
Microsoft 365 resources: 41.
Device resources: 4.
Backup inventory review complete: 45 checked, 1 require attention, 0 unclassified.
```

Detailed authentication, transport, pagination, and provider diagnostics can be enabled with:

```cmd
set SUPPORTTOOLKIT_DEBUG=true
```

Disable debug logging again with:

```cmd
set SUPPORTTOOLKIT_DEBUG=
```

---

# Testing

Run the complete test suite:

```cmd
dotnet test
```

Current BackupAggregator test coverage includes:

- Microsoft 365 classification
- device classification
- conflicting Microsoft 365 protection signals
- `notProtected` / `not_protected`
- scheduled resources without previous backups
- unknown Acronis states
- currently running backup state handling
- exact tenant resolution
- ambiguous tenant detection
- missing tenant handling
- Microsoft 365 leaf-group discovery
- Microsoft 365 pagination
- Microsoft 365 resource deduplication
- device inventory pagination
- canonical Acronis `AllMachines` inventory routing
- device resource deduplication
- accounting invariants
- healthy-resource suppression
- console reporting
- combined Microsoft 365 and device inventory regression behavior
- Acronis read-only transport restrictions

A validated production regression case currently produces:

```text
Microsoft 365 resources   41
Device resources           4
-----------------------------
Resources checked         45

Healthy                   44
Require attention          1
Unclassified               0
Coverage                45/45
```

Production data itself must never be committed as test fixtures unless it has been explicitly sanitized.

---

# Project structure

```text
src/
└── SupportToolkit/
    ├── Core/
    │   ├── Configuration/
    │   ├── ErrorHandling/
    │   ├── Logging/
    │   └── Modules/
    │
    ├── Modules/
    │   ├── BackupAggregator/
    │   │   ├── Models/
    │   │   ├── Reporting/
    │   │   └── Services/
    │   │
    │   └── BackupHealth/
    │
    ├── Providers/
    │   └── Acronis/
    │       ├── Alerts/
    │       ├── Devices/
    │       ├── Microsoft365/
    │       ├── ResourceManagement/
    │       ├── Tenants/
    │       └── Transport/
    │
    └── Program.cs

tests/
└── SupportToolkit.Tests/
```

---

# Current limitations

BackupAggregator currently reviews one explicitly selected tenant at a time.

It has been validated against the backup inventory currently used in the support workflow, but not against every Acronis workload category.

Areas that still require future validation include:

- additional workload categories exposed by different tenants,
- workload-specific views not currently used in the support workflow,
- semantics of long-running backup operations,
- partial tenant failures during multi-tenant review,
- behavior at larger tenant/resource counts.

The system should not silently expand its definition of healthy coverage without validating those assumptions first.

---

# Next milestone

The next major BackupAggregator milestone is:

```text
All-tenants backup review
```

The goal is to run the same validated review across the relevant tenant inventory and produce one consolidated exception report.

The single-tenant workflow remains the behavioral baseline:

```text
resolve tenant
    |
    v
fetch Microsoft 365 inventory
    |
    v
fetch device inventory
    |
    v
normalize
    |
    v
classify every resource
    |
    v
verify accounting
    |
    v
suppress healthy resources
    |
    v
surface exceptions
```

Multi-tenant execution should preserve those guarantees rather than weakening them for speed.