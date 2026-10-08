# SupportToolkit

SupportToolkit is a modular internal-support automation toolkit for reducing repetitive MSP support work through conservative, read-only automation.

## Current release

**v0.1.0**

The first usable SupportToolkit workflow is **BackupAggregator**, an Acronis backup inventory reviewer that can review either one customer tenant or the complete discovered customer estate and produce a consolidated exception report.

BackupHealth also exists in the repository, but remains experimental and is not considered an operational workflow.

---

# BackupAggregator

BackupAggregator replaces manual inspection of large backup reports with an exception-based review of the currently validated Acronis backup inventory.

Instead of displaying every healthy resource, it:

1. discovers the relevant customer tenant or tenants,
2. fetches the currently validated backup inventory,
3. normalizes Acronis resource types,
4. classifies every resource,
5. verifies that every fetched resource was accounted for,
6. suppresses healthy resources from detailed output,
7. surfaces resources requiring attention,
8. surfaces resources that cannot be classified safely,
9. reports tenant-level failures separately instead of silently skipping them.

The result is intended to answer:

```text
How many tenants were reviewed?
Did every tenant complete successfully?
How many backup resources were checked?
How many were healthy?
Which resources actually require attention?
Was every fetched resource accounted for?
```

---

## Example

```text
SUPPORTTOOLKIT // BACKUP REVIEW
===============================

SUMMARY
-------

Tenants attempted             12
Tenants reviewed              12
Tenants failed                 0
Tenants requiring attention    2
Backup resources checked     418
Healthy                      415
Require attention              3
Unclassified                   0
Resource coverage        418/418
Tenant coverage            12/12

ATTENTION REQUIRED
------------------

Customer Example
----------------

Resource                       Status                    Plan                        Last activity (UTC)
------------------------------ ------------------------  --------------------------  -------------------
EXAMPLE-SQL-01                 Not protected             SQL Database Backup (...)   2026-10-08 16:00

415 healthy backup resources were checked and intentionally suppressed from details.
```

The figures above are illustrative and are not application constants.

---

# Current inventory coverage

BackupAggregator currently reviews two Acronis inventory surfaces that have been validated against the existing support workflow:

```text
Acronis customer tenant
├── Microsoft 365 inventory
│   └── /bc/api/resource_manager/v1/o365/...
│
└── Device inventory
    └── /bc/api/resource_manager/v1/epm/resources
        using Acronis' built-in AllMachines group
```

## Microsoft 365

Microsoft 365 resources are discovered through Acronis Resource Manager group and resource endpoints.

The provider:

- discovers queryable Microsoft 365 leaf groups,
- follows pagination,
- combines resources from overlapping groups,
- removes duplicate resource identities,
- preserves resources without usable identities rather than silently discarding them.

A customer tenant may legitimately have **no Microsoft 365 workload configured**.

A successful Acronis response containing no Microsoft 365 groups is therefore treated as:

```text
Microsoft 365 resources: 0
```

rather than as a failed tenant review.

The tenant's device inventory is still reviewed normally.

## Device inventory

Device workloads are retrieved from the Acronis **All devices** inventory.

Acronis exposes this internally through its built-in `AllMachines` virtual group.

The returned inventory may contain resource types such as:

- machines,
- machines with agents,
- Hyper-V virtual machines,
- SQL-related resources,
- other device-oriented resource types exposed by Acronis.

One real-world machine may appear as multiple distinct Acronis resource objects.

BackupAggregator therefore counts **Acronis resource objects**, not assumed unique physical machines.

---

# Coverage limitation

BackupAggregator does **not** claim exhaustive coverage of every workload type supported by Acronis.

The Acronis interface can expose additional workload-specific sections, and the available sections can differ between customer tenants.

Examples may include:

- Hyper-V,
- Microsoft SQL,
- machines with agents,
- discovered devices,
- additional workload-specific views.

Some of these views represent resources already present in **All devices**. Others may become relevant for workloads not yet encountered or validated.

The current guarantee is therefore:

> **BackupAggregator covers the Acronis backup inventory surfaces currently validated against the support workflow.**

It is not:

> **Microsoft 365 + All devices universally represents every possible Acronis workload.**

Additional workload categories should be explicitly investigated before being considered safely covered.

---

# Classification philosophy

BackupAggregator is intentionally conservative.

A resource is only suppressed when the available Acronis data provides enough evidence to classify it as healthy.

The classifications are:

```text
Healthy
NeedsReview
Unknown
```

## Healthy

Healthy resources are counted but suppressed from detailed output.

Examples include:

```text
Microsoft 365:
lastTaskStatus = ok
lastTaskState  = idle
successful backup exists
```

and:

```text
Device:
state = idle / ok
previous successful backup exists
```

### Backup currently in progress

Acronis can expose an active device backup using states such as:

```text
backup
running
```

Acronis' own frontend treats these as an active backup operation rather than a backup failure.

BackupAggregator therefore treats:

```text
backup/running
+ previous successful backup
```

as healthy normal operation.

If no previous successful backup exists, the resource remains visible for review.

## NeedsReview

Resources remain visible when human attention is warranted or the available state is not safe to suppress.

Examples include:

```text
notProtected
not_protected
not_run
critical
error
warning
interaction
need_interaction
paused
canceled
cancelled
conflicting Microsoft 365 protection signals
unknown vendor states
```

A resource that is currently backing up but has never previously completed a successful backup also remains visible.

## Unknown

`Unknown` is used when the payload does not provide enough information to make a safe health determination.

Examples include:

- missing resource name,
- missing state,
- missing protection information,
- idle resource without successful-backup evidence.

Unknown resources remain visible.

---

# Accounting guarantees

BackupAggregator maintains two separate coverage concepts.

## Resource coverage

Every normalized resource must satisfy:

```text
Resources checked
=
Healthy
+ NeedsReview
+ Unknown
```

The report exposes this as:

```text
Resource coverage 418/418
```

If resource accounting fails, detailed resource output is stopped rather than presenting an incomplete result as trustworthy.

## Tenant coverage

Multi-tenant execution separately tracks:

```text
Tenants attempted
Tenants reviewed
Tenants failed
```

For example:

```text
Tenant coverage 12/12
```

A failed customer tenant can never silently disappear from the report.

If one tenant fails, successfully reviewed tenants remain available, but the result is explicitly marked as partial.

---

# Multi-tenant review

BackupAggregator can review every Acronis tenant explicitly identified as:

```text
kind = customer
```

Other tenant kinds are not currently inferred to be customer workloads.

Multi-tenant execution currently runs sequentially.

This is intentional for v0.1.0: correctness, failure isolation, and observable behavior are prioritized over maximum throughput.

For every selected customer tenant:

```text
customer tenant
      |
      v
Microsoft 365 inventory
      |
      v
device inventory
      |
      v
normalize
      |
      v
classify
      |
      v
verify accounting
      |
      +--> success
      |
      └--> isolated tenant failure
```

A failure in one tenant does not abort the complete estate review.

---

# Architecture

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

The review layer works with normalized inventory rather than Acronis transport details.

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

The current Acronis integration is intentionally designed not to modify customer environments.

---

# Running locally

## Requirements

- .NET 10 SDK

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

Enable production mode:

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

Never commit credentials to the repository.

---

## Review one tenant

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator inventory-review "TENANT NAME"
```

Example:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator inventory-review "Customer Example"
```

---

## Review all customer tenants

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator inventory-review --all
```

The all-tenant command:

- fetches the tenant catalogue once,
- selects `kind = customer`,
- reviews each selected tenant,
- isolates per-tenant failures,
- consolidates all findings into one report.

---

# Process exit codes

Backup findings themselves are valid report output and do not cause the process to fail.

Current BackupAggregator exit behavior:

```text
0   Review completed without tenant-level failures
1   Invalid command / usage
2   One or more tenant reviews were incomplete
```

This distinction allows future automation to differentiate:

```text
backup problems were found
```

from:

```text
the review itself did not complete
```

---

# Fixture mode

Fixture mode performs a deterministic local review without contacting Acronis.

Enable it with:

```cmd
set SUPPORTTOOLKIT_MODE=fixture
```

Run:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator
```

Production `inventory-review` commands are rejected while fixture mode is enabled rather than silently executing fixture data.

Return to production mode with:

```cmd
set SUPPORTTOOLKIT_MODE=production
```

---

# Debug logging

Normal BackupAggregator execution keeps operational logging concise.

Example:

```text
Starting all-tenant backup inventory review.
Customer tenants selected: 12.
Reviewing tenant 1/12.
Reviewing tenant 2/12.
...
All-tenant backup review complete: 12 attempted, 12 reviewed, 0 failed, 418 resources checked.
```

Detailed authentication, transport, pagination, and provider diagnostics can be enabled with:

```cmd
set SUPPORTTOOLKIT_DEBUG=true
```

Disable debug logging with:

```cmd
set SUPPORTTOOLKIT_DEBUG=
```

Debug output may contain additional technical information and should be handled accordingly.

---

# Testing

Run the complete suite with:

```cmd
dotnet test
```

At the v0.1.0 behavior freeze:

```text
Tests: 101
Failed: 0
Skipped: 0
```

Current test coverage includes behavior around:

- Microsoft 365 classification,
- device classification,
- conflicting Microsoft 365 protection signals,
- null Microsoft 365 inventory collections,
- tenants without Microsoft 365 configured,
- `notProtected`,
- `not_protected`,
- `not_run`,
- idle resources,
- scheduled resources without a previous backup,
- active `running` / `backup` operations,
- unknown vendor states,
- exact tenant resolution,
- customer-tenant selection,
- ambiguous tenant detection,
- missing tenant handling,
- Microsoft 365 leaf-group discovery,
- pagination,
- Microsoft 365 deduplication,
- device inventory pagination,
- canonical Acronis `AllMachines` routing,
- device deduplication,
- multi-tenant aggregation,
- per-tenant failure isolation,
- resource-accounting invariants,
- healthy-resource suppression,
- console reporting,
- read-only transport restrictions.

Production data must never be committed as test fixtures unless it has been explicitly sanitized.

---

# BackupHealth

BackupHealth is **experimental and still in progress**.

It originated during exploration of broader Acronis resource-status and alert APIs.

It is not currently considered an operational support workflow and should not be treated as equivalent in maturity to BackupAggregator.

Future SupportToolkit modules may replace or reuse parts of BackupHealth.

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

# v0.1.0 validation

Before the v0.1.0 behavior freeze, BackupAggregator was validated across a multi-tenant Acronis environment with complete tenant and resource accounting.

The release criteria included:

```text
multiple customer tenants reviewed
zero silent tenant failures
complete tenant accounting
complete resource accounting
healthy resources suppressed
actionable resources retained
unknown resources retained
```

The important guarantees are:

```text
every selected customer tenant is accounted for
every fetched backup resource is accounted for
healthy resources can be suppressed
exceptions remain visible
unknown data remains visible
failed tenant reviews remain visible
```

---

# Current limitations

BackupAggregator v0.1.0 intentionally leaves several areas for future work.

These include:

- validating additional Acronis workload categories,
- determining whether additional workload-specific inventory surfaces need explicit coverage,
- bounded concurrency for larger estates,
- richer handling of unusually long-running backup operations,
- export formats such as CSV or structured JSON,
- improved reporting and presentation,
- additional MSP support automation modules.

These are future enhancements rather than requirements for the v0.1.0 backup-review workflow.

---

# Development principle

SupportToolkit should prefer:

```text
visible uncertainty
```

over:

```text
silent assumptions
```

If a resource, tenant, workload type, or vendor state cannot be classified safely, the preferred behavior is to surface it rather than silently treating it as healthy.