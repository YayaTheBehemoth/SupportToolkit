
# SupportToolkit

[![Windows x64](https://img.shields.io/badge/Download-Windows_x64-blue?logo=windows)](https://github.com/YayaTheBehemoth/SupportToolkit/releases)

**SupportToolkit** is a modular .NET command-line application for automating repetitive Managed Service Provider (MSP) support tasks.

The project focuses on reducing manual investigation, improving operational consistency, and presenting actionable information from external service providers.

SupportToolkit currently includes:

- **BackupAggregator** — Consolidated Acronis backup inventory reviews.
- **BackupHealth** — Acronis backup health evaluation.
- **Zendesk ticketing** — Ticket generation and submission from backup reviews.
- **Monitoring scope** — Business Central subscription entitlement decides which Acronis tenants are reviewed.
- **Configuration management** — Reusable provider connections, runtime profiles, and secure credential storage.
- **Interactive CLI** — Menu-driven workflows alongside commands suitable for scripting.

The application supports fixture-based development and production integrations through the same underlying workflows.

**Current release:** [`v0.2.4`](https://github.com/YayaTheBehemoth/SupportToolkit/releases) (Windows x64 prerelease). See [Release history](#release-history) for what changed and why, and [Development branches](#development-branches) for work that is not part of `main` yet.

## Getting started

### Download for Windows

Download the latest available Windows x64 package from the [GitHub Releases page](https://github.com/YayaTheBehemoth/SupportToolkit/releases).

1. Download `SupportToolkit-win-x64.zip`.
2. Extract the archive to a directory of your choice.
3. Run `SupportToolkit.exe`.
4. Complete the configuration wizard when prompted.

The portable distribution is self-contained and does not require the .NET SDK or a separate .NET runtime.

SupportToolkit itself does not require an installation wizard or administrator-level installation.

### First-run configuration

When no configuration profiles exist, SupportToolkit automatically launches its configuration wizard.

The wizard guides you through:

1. Creating a runtime profile.
2. Selecting fixture or production mode for each module.
3. Configuring or reusing an Acronis connection when required.
4. Optionally configuring Zendesk ticket submission.
5. Selecting a Zendesk ticket destination.
6. Saving the configuration.

The first profile is activated automatically.

Subsequent profiles can be saved without changing the active profile.

**Fixture mode does not require production API credentials.** This allows the backup modules to be explored and tested without accessing external systems.

### Launching the application

Run:

```cmd
SupportToolkit.exe
```

The interactive menu provides access to BackupAggregator, BackupHealth, profile switching, configuration, and debug logging.

Direct CLI commands are also available for scripting and development.

### Command overview

| Command | Purpose | External effect |
|---|---|---|
| `SupportToolkit` | Interactive console (first run starts the configuration wizard). | Depends on the menu action chosen. |
| `SupportToolkit --help` | Print usage. | None. |
| `configure [status \| profile <name> \| profile delete <name> \| use <name>]` | Manage profiles, connections and credentials. | Local configuration and Windows Credential Manager only. |
| `backup-aggregator inventory-review <tenant> \| --all` | Consolidated Acronis inventory review. | Read-only in production mode. |
| `backup-aggregator ticket preview` | Generate and display a Zendesk ticket draft. | None. |
| `backup-aggregator ticket submit` | Create a Zendesk ticket from a completed review. | **Writes to Zendesk.** |
| `backup-aggregator scoped-review` | Review only tenants entitled to monitoring in Business Central, plus the operator's own tenant. | Read-only Acronis and Business Central calls; interactive Business Central sign-in. |
| `backup-aggregator ticket preview --scoped` / `ticket submit --scoped` | Ticket from a complete scoped review. | `submit` **writes to Zendesk.** |
| `backup-health` | Acronis backup health evaluation. | Read-only in production mode. |
| `dev acronis-tenants --production [--names]` | Tenant discovery diagnostics. | Read-only Acronis calls. |
| `dev usage-compare --production [--metric <name>] [--names]` | Read-only comparison of Business Central backup quantities with Acronis `storage_total`, per customer. | Read-only Business Central and Acronis calls. |
| `dev acronis-usage --production [--metric <name>]... [--tenant <text>] [--names [--out <path>]]` | Per-tenant Acronis usage probe for joining to other data. | Read-only Acronis calls. |
| `dev bc-subscriptions [--production]` | Business Central subscription evidence diagnostics. | Read-only; `--production` signs in interactively. |
| `dev customer-matching --production [--names] [--why]` | Dry run of the automatic customer-to-tenant matching. | Read-only Acronis and Business Central calls. |
| `dev monitoring-scope`, `dev customer-mapping` | Fixture-only scope and mapping walkthroughs. | None. |

Run any module with `--help` for its own usage text. Exit codes are listed per command below.

---

## Building from source

### Requirements

- Windows
- .NET 10 SDK
- Git

Clone the repository:

```cmd
git clone https://github.com/YayaTheBehemoth/SupportToolkit.git
cd SupportToolkit
```

Build:

```cmd
dotnet build
```

Run the automated tests:

```cmd
dotnet test
```

Start the interactive console:

```cmd
dotnet run --project src\SupportToolkit
```

To execute a raw CLI command, append `--` followed by the SupportToolkit command:

```cmd
dotnet run --project src\SupportToolkit -- configure status
```

The command examples below use the development workflow. For a portable installation, replace:

```text
dotnet run --project src\SupportToolkit --
```

with:

```text
SupportToolkit.exe
```

---

## BackupAggregator

BackupAggregator reviews Acronis backup inventory across individual customers or the discovered customer estate.

The module consolidates information from Acronis Microsoft 365 and device backup inventories into a single review.

Its purpose is to highlight exceptions and incomplete observations rather than require operators to inspect every healthy resource manually.

### Capabilities

- Discover customer tenants using the Acronis Account Management API.
- Review an individual tenant or all discovered customer tenants.
- Collect Microsoft 365 backup resources and device inventories.
- Follow paginated resource and tenant API responses.
- Normalize and deduplicate backup resource records.
- Identify healthy resources and resources requiring attention.
- Preserve resources that cannot be classified reliably.
- Isolate individual tenant failures without discarding successful reviews.
- Report resource accounting and tenant coverage.
- Generate Zendesk ticket drafts from completed reviews.

### Resource classification

BackupAggregator uses three classifications:

| Classification | Meaning |
|---|---|
| Healthy | Available evidence supports a healthy backup state. |
| NeedsReview | Available evidence indicates an issue requiring attention. |
| Unknown | Available evidence is insufficient for a reliable classification. |

Every normalized backup resource is accounted for:

```text
Resources checked = Healthy + NeedsReview + Unknown
```

Healthy resources are counted but generally suppressed from detailed exception reporting.

Unknown resources remain visible instead of being silently classified as healthy.

### Review completeness

A completed review must distinguish between successfully reviewed tenants and failed tenant reviews.

If individual tenants fail, BackupAggregator preserves the successful results but explicitly identifies the review as partial.

If customer discovery unexpectedly returns zero customer tenants, the all-tenant review fails rather than reporting an apparently healthy empty estate.

Ticket creation is blocked when the underlying review is incomplete or its resource accounting is inconsistent.

**Waiting for in-progress resources.** A resource that Acronis reports as running is shown as In progress, and a ticket drafted mid-backup would differ from one drafted afterwards. In production, the review therefore waits for running resources and re-reviews only the tenants that still have some. It prints how many resources are still running and how long it has waited. A tenant whose re-review fails is a failed tenant review like any other. When the maximum wait ends, the remaining resources stay In progress, are counted as pending in the report, console and ticket, and are never shown as healthy. Whether a ticket can still be created in that case is unchanged: it can. With nothing running there is no wait and no extra call. The settings are per profile and module:

```json
"review": {
  "backup-aggregator": {
    "inProgressMaxWaitSeconds": 1800,
    "inProgressPollIntervalSeconds": 30
  }
}
```

Both values are optional and default to 1800 and 30. `0` for the maximum wait disables waiting. The single-tenant `inventory-review <tenant>` does not wait. `configure status` shows the effective values.

### Commands

Review one tenant:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator inventory-review "Example Customer"
```

Review all discovered customer tenants:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator inventory-review --all
```

In fixture mode, the following shortcut is also available:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator
```

### Exit codes

| Code | Meaning |
|---|---|
| 0 | Review completed without tenant-level failures. |
| 1 | Invalid command or execution error. |
| 2 | One or more tenant reviews were incomplete. |

Individual backup findings do not automatically cause an execution error.

### Inventory limitations

BackupAggregator currently collects the Acronis Microsoft 365 and device resource inventories implemented by its providers.

Acronis inventory objects do not necessarily correspond one-to-one with physical devices or protected workloads.

The module reports the information available from these sources; it is not a guarantee that every possible Acronis workload type has been inspected.

Pagination and response-contract safeguards are tested with synthetic API responses. Some API behaviors have also been validated against live Acronis responses, but not every endpoint and pagination scenario has been observed in production.

### Microsoft 365 workload accounting

Acronis can report a Microsoft 365 Team and its associated Group as separate resource records.

- A Group associated with a Team is kept as an independently monitored workload when it carries its own protection evidence, such as a protected SharePoint site.
- A matching Group without that evidence is consolidated with its Team, and its health evidence is merged rather than hidden behind a healthy Team.
- Failed or unknown evidence on a consolidated entry stays visible.

This rule is deliberately narrow. It reflects discrepancies observed in live Acronis data and is covered by synthetic regression tests, but it does not establish that every Acronis resource shape has been exercised. It was last changed in `v0.2.4`.

Acronis inventory counts and the Acronis portal's "protected workloads" figure can legitimately differ, because BackupAggregator reports every inventoried resource.

### Monitoring scope

`backup-aggregator scoped-review` reviews only Acronis tenants whose Business Central customer is entitled to monitoring, plus the operator's own tenant, which is billed to no one and is always included. It is recognized through its partner-kind tenant, which Acronis does not allow the toolkit to review, so the matching customer-kind tenant is the one reviewed.

**Entitlement rule:** a customer is entitled when it has an Active, unblocked subscription with a line for one of the configured entitling items. The add-on item alone is enough; the base backup item isn't required. An entitling item that appears only on Suspended subscriptions does not entitle. A confirmed customer that isn't entitled and isn't monitored is consistent, not an exception.

Customers are matched to Acronis tenants automatically on every run, by name, with nothing stored. Unique strong matches are accepted automatically; ambiguous or conflicting ones are left unmatched. Customers that hold one of the configured service items (for backups, the backup items themselves) are matched too, so a tenant whose customer has the backup but not the monitoring add-on is decided as not entitled instead of staying unresolved.

The run prints a scope report. The default view shows the summary, any notices and the tenants in scope this run; with `SUPPORTTOOLKIT_DEBUG=true` it lists every section:

1. **Summary.** The Acronis customer tenants are split into in scope, excluded, needs scope review and unscoped, and those four counts add up to the total. The summary also gives entitled customers, discrepancies, ambiguous results and unscoped Business Central records.
2. **In scope this run.** Each tenant reviewed, with its Business Central customer and the entitling subscription and item.
3. **Excluded.** Each tenant left out, with the reason: excluded by configuration, or not entitled.
4. **Discrepancies.** These are only computed, never ticketed.
5. **Exceptions.** Tenants needing scope review, Ambiguous and Unscoped results, and configuration notices.
6. **Automatic mapping.** Accepted links by method, pending and ambiguous cases, and entitled customers that have no Acronis tenant.

**Tickets.** `backup-aggregator ticket preview --scoped` and `ticket submit --scoped` (and the matching interactive menu entries) build the same ticket as a full review, with a scope line, from the scoped review. A ticket is produced only when nothing but unmatched tenants is left open: no scope review pending, no notice, no entitled customer without a tenant and no failed tenant review. Otherwise the reasons are printed and nothing is drafted or sent. Tenants that could not be matched to a customer do not block the ticket, because they are not reviewed and the ticket says so itself: it starts with an `INCOMPLETE SCOPE` banner naming how many tenants were not covered, its subject is prefixed `[INCOMPLETE SCOPE]`, and they are listed by name under STRAY TENANT CHECK. The console still reports the scope as incomplete.

The ticket's SCOPE section lists every in-scope tenant by name, sorted, with the operator's own tenant marked. A STRAY TENANT CHECK block follows. It is informational and never blocks a ticket. Leak candidates are Acronis tenants matched to a Business Central customer that holds neither the backup item nor the monitoring item, so they may be running without being billed. Customers with the backup item but no monitoring item are a legitimate exclusion and are not listed. Unmatched tenants are listed there as well, with the banner above. The leak check needs service items to be configured. Without them it states "unavailable" instead of reporting none, and name matching stays limited to customers holding an entitling or service item. With them, matching also considers every customer that has any subscription, using data already read, with no extra call. The same section appears in the console report in both the default and the detailed view.

The report is deliberately naive. Every discrepancy it finds is reported on every run. It doesn't track which ones are already known, accepted or being fixed; that judgment stays with the operator.

Tenant names come from the live Acronis data at runtime and are never stored.

No source for which tenants are actually monitored exists yet. Without one, "monitored without entitlement" can't be detected, and the report says so.

Settings live per module under the profile's `monitoringScope` section. The values below are fictional:

```json
"monitoringScope": {
  "backup-aggregator": {
    "monitoringItemNumbers": [ "EXAMPLE-ADDON" ],
    "serviceItemNumbers": [ "EXAMPLE-BACKUP", "EXAMPLE-BACKUP-2" ],
    "excludedTenantIds": [ "00000000-0000-0000-0000-000000000001" ]
  }
}
```

`serviceItemNumbers` is optional. Without it only entitled customers are matched, and tenants of other customers stay unresolved. A customer that is entitled but holds no service item is flagged as an inconsistency.

**Business Central connection.** Add it with `configure`, or in `connections.businessCentral`. It is a public client with delegated sign-in: no secret is stored and there are no environment overrides. The values below are fictional:

```json
"businessCentral": {
  "example-bc": {
    "tenantId": "00000000-0000-0000-0000-000000000001",
    "clientId": "00000000-0000-0000-0000-000000000002",
    "environment": "example-environment",
    "companyId": "00000000-0000-0000-0000-000000000003",
    "companyName": "Example Company",
    "apiPublisher": "example",
    "apiGroup": "subscriptions",
    "apiVersion": "v1.0"
  }
}
```

`companyName` is optional. Reference the connection from a profile with `businessCentralConnection`. Because the token cache is in memory, each run asks you to sign in again. See `local.config.example.json` for a complete profile.

**How names are matched.** Matching ignores case, accents and punctuation (so `Å` and `aa` are the same), apostrophes and invisible characters, and stray spaces. Long names may differ by one typo, and words may differ by a short ending (`Northwind Traders` against `Northwind Trader`). Numbers must match exactly. A name that contains the other plus extra words (`Northwind Traders` against `Northwind Traders Holding`) is a containment match. Exact, near-exact and containment matches are accepted only when unique; weak overlaps, ambiguity and key conflicts are never accepted.

**Diagnostics.** `dev customer-matching --production` prints the matching result as counts. `--names` adds runtime names, and `--why` explains each unmatched tenant (for example `Northwind Traders` against `Northwind  Traders ApS`, differing by stray spacing and an extra word). Output is for local use and must not be pasted into public places.

Interactive mode offers only the scoped review. The unmonitored all-tenant review remains available as a command. On exit, the session's sign-in is dropped, and you are told to sign in again next time.

---

## BackupHealth

BackupHealth evaluates Acronis backup health information and presents exceptions requiring operator attention.

The workflow:

1. Retrieves backup resource and diagnostic information.
2. Constructs a normalized backup health snapshot.
3. Evaluates resources against backup health rules.
4. Presents exceptions and supporting diagnostics in the console.

The current evaluation includes stale backup activity detection using a 48-hour threshold.

### Command

```cmd
dotnet run --project src\SupportToolkit -- backup-health
```

BackupHealth supports fixture and production modes.

It is separate from BackupAggregator: BackupHealth evaluates backup health conditions, while BackupAggregator produces consolidated inventory reviews.

---

## Zendesk ticketing

SupportToolkit includes a shared ticketing capability currently used by BackupAggregator.

Ticketing is not an independent operational module. It is an integration capability available to workflows that need to generate or submit tickets.

### Ticket workflow

BackupAggregator can:

- Generate a ticket draft from a completed backup review.
- Preview the subject and internal ticket content.
- Submit the ticket to a configured Zendesk destination.
- Display the resulting ticket ID and browser link.
- Identify an idempotent replay when Zendesk explicitly reports one.

Tickets include review findings and unclassified resources while suppressing healthy resource details.

### Preview a ticket

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator ticket preview
```

A preview does not create a Zendesk ticket.

### Submit a ticket

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator ticket submit
```

**This command performs a real external write.**

Submission requires:

- A configured Zendesk connection.
- Positive submitter and requester user IDs on that connection.
- A selected destination with a valid group ID.
- Valid Zendesk credentials.
- A complete backup review suitable for ticket generation.
- An explicitly invoked ticket submission operation.

The raw CLI command is noninteractive and does not request an additional confirmation.

The interactive console displays the resolved Zendesk instance, destination, group ID, and review summary before requesting confirmation.

If the destination changes between interactive confirmation and submission, SupportToolkit blocks the submission.

### Idempotency and retries

Zendesk submissions include a deterministic idempotency key derived from the ticket contents and destination.

When Zendesk confirms an idempotent replay, SupportToolkit displays the existing ticket rather than claiming a new ticket was created.

Idempotency is limited by the external provider's retention behavior. It is not permanent ticket deduplication.

SupportToolkit does not automatically retry ticket creation POST requests.

If a request times out, fails, or returns an invalid response, ticket creation may still have succeeded.

**Check the selected Zendesk destination for an existing ticket before retrying an unconfirmed submission.**

---

## Configuration management

SupportToolkit separates runtime profiles, reusable provider connections, ticket destinations, and credentials.

### Runtime profiles

A profile defines:

- The runtime mode of each module.
- An optional Acronis connection reference.
- An optional Zendesk connection reference.
- The Zendesk destination to use for ticket submission.

Each module can independently use either:

| Mode | Behavior |
|---|---|
| `fixture` | Use synthetic development data without provider API calls. |
| `production` | Use the configured external provider integration. |

A profile can use fixture data for one module and production data for another.

### Provider connections

Provider connections contain reusable integration metadata.

An Acronis connection defines the API datacenter and client identity.

A Zendesk connection defines the Zendesk instance and client identity. It also defines the Zendesk users that created tickets are attributed to. See [Ticket submitter and requester](#ticket-submitter-and-requester).

Multiple profiles may reference the same connection without duplicating its credentials.

### Zendesk destinations

Each Zendesk connection may contain multiple named ticket destinations.

A destination maps to a Zendesk group ID within that instance.

For example, separate destinations may be configured for development testing and operational ticket routing.

Selecting a destination does not create a ticket. It only determines where an explicitly submitted ticket will be routed.

### Ticket submitter and requester

Every Zendesk connection requires two non-secret settings:

| Setting | Zendesk field | Meaning |
|---|---|---|
| `submitterId` | `submitter_id` | User ID of the agent the ticket and its first comment are submitted as. |
| `requesterId` | `requester_id` | User ID of the ticket's requester. |

Both must be positive Zendesk user IDs within the connection's instance. They may be the same user, but they are configured separately.

Without these fields Zendesk attributes the ticket to the user the OAuth token acts as. With the `client_credentials` grant, that is the user who created the OAuth client. SupportToolkit therefore always sends both and never falls back to the token's identity.

If either value is missing or not positive, ticket preview of the destination and submission fail locally before any Zendesk request. `configure status` shows the reason.

Set them with the configuration wizard:

```cmd
dotnet run --project src\SupportToolkit -- configure profile <profile>
```

The wizard asks for them when creating a connection or updating its metadata, and automatically when an existing connection lacks them.

Alternatively, edit the configuration file directly:

```json
"zendesk": {
  "example-sandbox": {
    "subdomain": "example",
    "clientId": "example-zendesk-client-id",
    "submitterId": 900000000001,
    "requesterId": 900000000001,
    "destinations": { "dev": { "groupId": 111111 } }
  }
}
```

**Upgrading:** Zendesk connections saved by earlier versions do not have these settings. Ticket submission through them is unavailable until both are added.

These settings are not part of the ticket idempotency key.

### Managing profiles

Inspect configuration:

```cmd
dotnet run --project src\SupportToolkit -- configure status
```

Create or edit a profile:

```cmd
dotnet run --project src\SupportToolkit -- configure profile local-dev
```

Run the wizard for the current/default profile:

```cmd
dotnet run --project src\SupportToolkit -- configure
```

Activate a profile:

```cmd
dotnet run --project src\SupportToolkit -- configure use local-dev
```

Delete an inactive profile:

```cmd
dotnet run --project src\SupportToolkit -- configure profile delete old-profile
```

Editing or creating a non-active profile does not automatically activate it.

The wizard offers an explicit activation choice. The standalone `configure use` command is also available.

Deleting a profile does not delete shared provider connections or their stored credentials.

### Configuration status

The status command distinguishes:

- Effective runtime module modes.
- Saved profile settings.
- Selected Acronis and Zendesk connections.
- Zendesk destination and group routing.
- Zendesk ticket submitter and requester user IDs.
- Environment overrides.
- Locally detectable missing or invalid configuration.

Configuration readiness checks do not authenticate with external providers or perform API requests.

A configuration reported as valid locally has not necessarily been verified against the live service.

### Configuration storage

Debug builds use:

```text
<repository-root>\local.config.json
```

Portable Release builds use:

```text
%LOCALAPPDATA%\SupportToolkit\local.config.json
```

The configuration file contains non-secret profile and connection metadata.

Authentication secrets are stored separately through Windows Credential Manager.

The configuration path may be overridden with:

```cmd
set SUPPORTTOOLKIT_CONFIG=C:\Path\To\config.json
```

The development configuration file is excluded from Git.

An example file is provided in `local.config.example.json`. All values in it are fictional.

### Retired write switch

Earlier versions had a global `allowWrites` profile setting and a `SUPPORTTOOLKIT_ALLOW_WRITES` environment variable. Both are retired:

- Existing configuration files that still contain `allowWrites` load normally, and the property is dropped the next time the file is saved.
- `SUPPORTTOOLKIT_ALLOW_WRITES` is ignored. `configure status` reports it as obsolete if it is still set.

The switch was removed because it duplicated the safeguards that actually gate a write: an explicitly invoked submit operation, a configured Zendesk connection, a valid destination, and (in the interactive console) a confirmation prompt.

---

## Environment overrides

Runtime modes can be overridden independently of saved profile settings.

For example:

```cmd
set SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE=fixture
set SUPPORTTOOLKIT_BACKUP_HEALTH_MODE=production
```

Supported values are `fixture` and `production`.

Clear an override in Windows CMD:

```cmd
set SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE=
set SUPPORTTOOLKIT_BACKUP_HEALTH_MODE=
```

Provider configuration also supports selected environment overrides.

### Acronis

```text
ACRONIS_DATACENTER_URL
ACRONIS_CLIENT_ID
ACRONIS_CLIENT_SECRET
```

Providing all three allows an environment-defined Acronis connection without requiring a stored provider connection.

### Zendesk

```text
ZENDESK_SUBDOMAIN
ZENDESK_CLIENT_ID
ZENDESK_CLIENT_SECRET
```

Zendesk ticket destinations must still be configured through an active profile.

A Zendesk subdomain override cannot silently redirect ticket submission to another instance while retaining the configured destination.

### Override precedence

Effective runtime settings are resolved from environment overrides and the active profile.

Run:

```cmd
dotnet run --project src\SupportToolkit -- configure status
```

to inspect the effective settings before running a production workflow.

Environment variables should not be used to store production credentials in shared scripts or committed files.

---

## Debug logging and diagnostics

SupportToolkit provides optional diagnostic logging for troubleshooting.

Enable it in Windows CMD:

```cmd
set SUPPORTTOOLKIT_DEBUG=true
```

Disable it:

```cmd
set SUPPORTTOOLKIT_DEBUG=
```

Debug output includes operational information such as request methods, endpoint categories, response status codes, execution timings, and resource counts.

Additional safeguards include:

- UUID redaction in operational logging.
- Single-line diagnostic messages.
- Structured tenant-failure diagnostics.
- Exception type and HTTP status reporting without raw debug stack traces.
- Explicit reporting of incomplete tenant reviews.

These measures reduce accidental information disclosure but do not make arbitrary production output safe to publish.

Operator-facing reports can contain tenant names, resource names, and other operational data. Review and sanitize captured output before sharing it.

---

## Acronis tenant discovery diagnostics

SupportToolkit includes a developer utility for inspecting Acronis tenant discovery independently of the backup modules.

This performs a read-only production API operation:

```cmd
dotnet run --project src\SupportToolkit -- dev acronis-tenants --production
```

It reports tenant population and contract diagnostics, including invalid identities, missing fields, and duplicate identifiers.

The command exits with code `2` and prints a warning when the tenant directory has completeness or identity anomalies, such as an empty directory, invalid or duplicate identifiers, or missing names or kinds. Duplicate customer display names are reported as observations and do not by themselves cause exit code `2`.

To additionally display customer names for local membership verification:

```cmd
dotnet run --project src\SupportToolkit -- dev acronis-tenants --production --names
```

Customer names are not displayed unless `--names` is explicitly supplied.

This command uses the active profile's configured Acronis connection and does not require changing BackupAggregator or BackupHealth to production mode.

Production diagnostic output should be handled as potentially sensitive.

---

## Acronis usage probe

A second developer utility reads per-tenant usage from Acronis, in a form meant to be joined to other data. It only reads and prints: it does not call Business Central, compare anything, or feed any report.

```cmd
dotnet run --project src\SupportToolkit -- dev acronis-usage --production
```

The default output is aggregate and safe to share: customer tenant counts, how many have usage, how many have none returned (unknown, never zero), the metric names and units seen, and the spread of values per metric. Units are printed exactly as Acronis reports them and are never converted.

For one row per customer tenant and measurement (tenant id and name, parent tenant, metric, edition, type, infra id, application id, value, absolute value, unit, period start and a status column). `--metric <name>` (repeatable, case-insensitive) keeps only the named metrics; a tenant without them gets an `unknown` row:

```cmd
dotnet run --project src\SupportToolkit -- dev acronis-usage --production --names
dotnet run --project src\SupportToolkit -- dev acronis-usage --production --names --out C:\private\usage.csv
```

Rows contain customer names. `--out` writes a new CSV and refuses a path inside any git working tree, an existing file, or a missing folder. Tenants with no usage returned get an explicit `unknown` row, so they cannot drop out of a join. The command exits with code `2` when any tenant or value is unknown.

It uses one read-only endpoint, the batch usage endpoint (`GET /api/2/tenants/usages`), added to the read-only allowlist as an exact path. Acronis publishes no measurement timestamp for it, only the start of the usage period, and describes the figures as refreshed every few hours and not meant for billing. The batch size limit and required API-client role are not documented, so requests are chunked at 50 tenants and any failed chunk fails the whole run. The metric names, and which value is the right one, are for the operator to decide from the output.

---

## Interactive console

Launching SupportToolkit without arguments opens its interactive interface:

```cmd
dotnet run --project src\SupportToolkit
```

The main menu provides:

- BackupAggregator
- BackupHealth
- Profile switching
- Configuration wizard
- Debug logging toggle

The BackupAggregator menu offers the monitored review (the tenants Business Central entitles, plus the operator's own), ticket preview, and destination-aware ticket submission from that review. It needs a Business Central connection in the active profile, or fixture mode. The all-tenant review and its ticket are not in the menu; they remain available as `inventory-review` and `ticket preview` / `ticket submit` on the command line for development.

Interactive and raw CLI operations share the same application workflows.

---

## Architecture

SupportToolkit separates operational modules, external providers, shared infrastructure, and user interfaces.

```text
src/
└── SupportToolkit/
    ├── Program.cs
    │
    ├── Cli/
    │   └── Commands/
    │
    ├── Ui/
    │   └── Interactive/
    │
    ├── Core/
    │   ├── Configuration/
    │   ├── Secrets/
    │   ├── MonitoringScope/
    │   ├── CustomerMapping/ (Matching/ inside)
    │   ├── Reconciliation/
    │   ├── Ticketing/
    │   ├── Logging/
    │   ├── ErrorHandling/
    │   └── Modules/
    │
    ├── Modules/
    │   ├── BackupAggregator/
    │   │   ├── Fixtures/
    │   │   ├── Models/
    │   │   ├── MonitoringScope/
    │   │   ├── Reporting/
    │   │   ├── Services/
    │   │   └── Ticketing/
    │   │
    │   └── BackupHealth/
    │       ├── Evaluation/
    │       ├── Models/
    │       ├── Reporting/
    │       └── Services/
    │
    └── Providers/
        ├── Acronis/
        ├── BusinessCentral/
        └── Zendesk/

tests/
└── SupportToolkit.Tests/

scripts/
└── publish-windows.cmd
```

### Modules

Modules implement operational workflows and own their domain-specific logic, models, and reporting.

### Providers

Providers handle integration with external systems.

Acronis integrations are separated by responsibility, including tenant discovery, Microsoft 365 resources, devices, and backup health data.

Zendesk implements the shared ticket-provider capability.

Business Central supplies subscription evidence and customer names through neutral Core contracts, so any module can scope itself by entitlement with its own item numbers.

### Core

Core contains shared configuration, secure credential storage, logging, error handling, and ticketing abstractions.

### CLI and UI

The raw CLI and interactive console are separate presentation layers over the same workflows.

---

## Security principles

SupportToolkit is designed around explicit external-operation boundaries.

### Acronis

Acronis requests use a restricted HTTP transport that permits only approved read endpoints and the authentication requests necessary to obtain access tokens.

Unapproved endpoints, methods, and origins are rejected before transmission.

### Zendesk

Ticket creation is an explicit write operation.

A configured connection alone does not authorize automatic ticket creation.

Submission requires valid destination routing, and the interactive console additionally requests confirmation.

Tickets are always attributed to explicitly configured submitter and requester user IDs, never implicitly to the OAuth token's identity.

### Business Central

Business Central is read-only. Its transport permits only GET requests to an allowlist of subscription and customer-name endpoints, on a pinned host, authority and scope. Sign-in is delegated and interactive on every run; the access token lives in memory only, and no secret is stored.

### Credentials

Authentication secrets are stored separately from configuration metadata using Windows Credential Manager.

Credentials, tokens, customer data, and unsanitized diagnostic output must not be committed to the repository.

### Fixture-driven development

Synthetic fixtures are used to develop and test provider behavior without depending on production environments.

Fixture tests validate the application's behavior against known inputs. They do not, by themselves, establish that an external API behaves identically in production.

Live integration validation is performed separately when authorized.

---

## Testing

Run the full automated test suite:

```cmd
dotnet test
```

Run a Release build and test suite:

```cmd
dotnet build -c Release
dotnet test -c Release --no-build
```

The automated tests cover areas including:

- Backup resource normalization and classification.
- Tenant discovery and resolution.
- Microsoft 365 and device inventory contracts.
- Pagination and duplicate handling.
- Resource and tenant accounting.
- Concurrency and tenant-failure isolation.
- Ticket draft generation and review completeness.
- Zendesk destination validation and routing.
- Idempotent ticket replay reporting.
- Submission failures and uncertain outcomes.
- Profile and configuration behavior.
- Logging and exception diagnostics.
- Acronis transport restrictions.
- Waiting for in-progress resources, and the usage probe and its read-only endpoint.
- Monitoring entitlement, scope completeness and operator-tenant handling.
- Customer-to-tenant name matching, automatic mapping and reconciliation.
- Scoped tickets and their completeness gate.
- Business Central transport restrictions and customer-name reads.

Synthetic integration tests and live vendor validation are distinct forms of evidence.

---

## Publishing a Windows release

A Windows x64 distribution can be built using:

```cmd
scripts\publish-windows.cmd
```

The publishing script:

1. Builds the application in Release configuration.
2. Runs the automated tests.
3. Publishes a self-contained Windows x64 executable.
4. Packages the published application into a ZIP archive.
5. Validates the ZIP archive and its contents.

The generated package is:

```text
artifacts\SupportToolkit-win-x64.zip
```

The published executable is located under:

```text
artifacts\publish\win-x64\SupportToolkit.exe
```

The ZIP includes the executable and required supporting files, including bundled fixture data.

### Updating a portable installation

To update SupportToolkit:

1. Download a newer release.
2. Extract the new distribution archive.
3. Launch the updated executable.

By default, portable releases keep configuration and credentials outside the application directory, allowing an application update without deleting stored profiles or secrets.

---

## Release history

Releases are published as Windows x64 prereleases on the [GitHub Releases page](https://github.com/YayaTheBehemoth/SupportToolkit/releases).

| Version | Tagged commit date | Summary | Why |
|---|---|---|---|
| `v0.3.0` | 2026-10-27 | Business Central monitoring scope: entitlement from subscription records decides which Acronis tenants are reviewed, with automatic name-based customer matching, always-included operator tenant, scope completeness reporting and scoped tickets. Interactive console offers the scoped review only. | The review needed to cover exactly the customers entitled to monitoring, not every tenant. |
| `v0.2.4` | 2026-10-19 | Corrected Teams and SharePoint workload accounting in BackupAggregator. | Comparisons against live data showed Team-associated Groups with their own protected SITE workload being collapsed into the Team, undercounting resources. |
| `v0.2.3` | 2026-10-17 | Stabilization baseline: provider contract and pagination hardening, fail-closed ticketing and configuration behavior, read-only `dev acronis-tenants` diagnostics, retired `allowWrites`, redacted diagnostics, reorganized configuration code, consolidated tests. | Live Acronis observations and a review of failure paths showed that incomplete or contradictory data must never read as a clean result. |
| `v0.2.2` | 2026-10-13 | Zendesk destination routing, Teams backing-group normalization, configuration and debug controls in the interactive console. | Tickets needed to reach an explicitly chosen group without duplicating credentials across profiles. |
| `v0.2.1` | 2026-10-11 | Windows-compatible ZIP packaging with extraction and startup checks in the release workflow. | The first ZIP did not open cleanly with Windows Explorer. |
| `v0.2.0` | 2026-10-11 | First automated Windows release. Adds the interactive console, configuration profiles and wizard, and Zendesk ticketing from BackupAggregator, with CI and a GitHub Releases workflow. | Colleagues needed a portable executable that is built and tested the same way every time, not assembled by hand. |
| `v0.1.0` | 2026-10-09 | Initial tag: BackupHealth and BackupAggregator as raw CLI commands. | |

Behavior changes that matter to operators:

- **Partial reviews are explicit.** A tenant that fails to review is reported as incomplete and the run exits with code `2`. Ticket creation is blocked for incomplete reviews.
- **Zero discovered customers is a failure,** not an empty healthy estate.
- **Ticket destinations are explicit.** A profile selects one named Zendesk destination, and destination identity is part of the idempotency key.
- **Diagnostics are redacted.** UUIDs are redacted in operational logging, and debug output reports exception types and HTTP statuses rather than raw messages or stack traces.

## Development branches

One feature branch contains work that is **not** in `main` or in any release:

| Branch | Contents | Evidence boundary |
|---|---|---|
| `feature/compliance-investigation` | A synthetic Intune and Microsoft Graph compliance module: exception-first device list, setting-level evidence, device investigation and a consolidated multi-tenant `compliance report`. | Uses synthetic fixtures. No live Graph authorization, tenant coverage or case lifecycle automation has been established. |

Synthetic fixtures do not prove real API contracts, authorization or completeness.

## Project status

SupportToolkit is under active development.

BackupAggregator, BackupHealth, and Zendesk ticket submission are implemented on `main`, with automated test coverage and selected production integration validation:

- Acronis production reads and comparisons against the Acronis portal have been exercised for BackupAggregator and tenant discovery.
- Business Central reads (subscriptions and customer names) and the automatic matching were exercised with read-only commands against a live environment; the scoped review has had limited end-to-end validation.
- Zendesk ticket routing and idempotent replay were validated in a personal trial account. Authorized end-to-end validation in an organizational Zendesk environment has not been completed.

Provider behavior is only considered production-validated where real external responses have been observed and checked.

Deployment model, scheduling, persistence and an operator-friendly interface are intentionally undecided. Different modules have different operational needs, and the current interactive console has usability limits for non-developers. These will be revisited when a validated workflow needs them rather than designed up front.

The current focus is correctness, maintainability, explicit operator control, and practical usefulness in MSP support workflows.

Sanitized design decisions, operational discoveries and validation notes are kept in the public [SupportToolkitNotes](https://github.com/YayaTheBehemoth/SupportToolkitNotes) repository. This repository's source and tests are authoritative for implementation.
