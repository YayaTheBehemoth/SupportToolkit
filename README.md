
# SupportToolkit

[![Windows x64](https://img.shields.io/badge/Download-Windows_x64-blue?logo=windows)](https://github.com/YayaTheBehemoth/SupportToolkit/releases)

**SupportToolkit** is a modular .NET command-line application for automating repetitive Managed Service Provider (MSP) support tasks.

The project focuses on reducing manual investigation, improving operational consistency, and presenting actionable information from external service providers.

SupportToolkit currently includes:

- **BackupAggregator** — Consolidated Acronis backup inventory reviews.
- **Zendesk ticketing** — Ticket generation and submission from backup reviews.
- **Monitoring scope** — Business Central subscription entitlement decides which Acronis tenants are reviewed.
- **Configuration management** — Reusable provider connections, runtime profiles, and secure credential storage.
- **Interactive CLI** — Menu-driven workflows alongside commands suitable for scripting.
- **Usage report** — Acronis storage per customer against what Business Central bills for backup, for the monthly reconciliation.
- **Web UI** — A local browser interface for the Backup Aggregator, Zendesk bulk delete and the usage report, started with one double-click or `SupportToolkit serve`.

The application supports fixture-based development and production integrations through the same underlying workflows.

**Current release:** [`v0.4.0`](https://github.com/YayaTheBehemoth/SupportToolkit/releases) (Windows x64 prerelease). See [Release history](#release-history) for what changed and why, and [Development branches](#development-branches) for work that is not part of `main` yet.

## Getting started

### Download for Windows

Download the latest available Windows x64 package from the [GitHub Releases page](https://github.com/YayaTheBehemoth/SupportToolkit/releases).

1. Download `SupportToolkit-win-x64.zip`.
2. Extract the archive to a directory of your choice.
3. Double-click `Start SupportToolkit.cmd` to open the web UI, or run `SupportToolkit.exe` for the console.
4. Before first use, create a profile with the configuration wizard (`SupportToolkit configure`), or, if you were given a settings file, double-click `Import config.cmd` (see [Configuration storage](#configuration-storage)).

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

The interactive menu provides access to BackupAggregator, profile switching, configuration, and debug logging.

Direct CLI commands are also available for scripting and development.

### Command overview

| Command | Purpose | External effect |
|---|---|---|
| `SupportToolkit` | Interactive console (first run starts the configuration wizard). | Depends on the menu action chosen. |
| `SupportToolkit --help` | Print usage. | None. |
| `serve [--port <n>] [--no-browser]` | Open the local web UI. See [Web UI](#web-ui). | Listens on 127.0.0.1 only. What the pages do depends on the active profile's mode. |
| `configure [status \| profile <name> \| profile delete <name> \| use <name> \| import [file]]` | Manage profiles, connections and credentials. `import` moves a settings file into place (see [Configuration storage](#configuration-storage)). | Local configuration and Windows Credential Manager only. |
| `backup-aggregator inventory-review <tenant> \| --all` | Consolidated Acronis inventory review. | Read-only in production mode. |
| `backup-aggregator ticket preview` | Generate and display a Zendesk ticket draft. | None. |
| `backup-aggregator ticket submit` | Create a Zendesk ticket from a completed review. | **Writes to Zendesk.** |
| `backup-aggregator scoped-review` | Review only tenants entitled to monitoring in Business Central, plus the operator's own tenant. | Read-only Acronis and Business Central calls; interactive Business Central sign-in. |
| `backup-aggregator ticket preview --scoped` / `ticket submit --scoped` | Ticket from a complete scoped review. | `submit` **writes to Zendesk.** |
| `dev acronis-tenants --production [--names]` | Tenant discovery diagnostics. | Read-only Acronis calls. |
| `usage-report [--names]` | Acronis storage per customer against Business Central billing for the backup items. See [Usage report](#usage-report). | Read-only Business Central and Acronis calls in production mode; interactive Business Central sign-in. |
| `dev usage-compare --production [--metric <name>] [--names]` | Read-only comparison of Business Central backup quantities with Acronis `storage_total`, per customer. The developer forerunner of `usage-report`. | Read-only Business Central and Acronis calls. |
| `dev acronis-usage --production [--metric <name>]... [--tenant <text>] [--names [--out <path>]]` | Per-tenant Acronis usage probe for joining to other data. | Read-only Acronis calls. |
| `dev bc-subscriptions [--production]` | Business Central subscription evidence diagnostics. | Read-only; `--production` signs in interactively. |
| `dev zendesk-bulk-delete --production [--organization <name>] [--sender <name>] [--requester <email>] [--domain <domain>] [--text <text>] [--dry-run \| --delete [--include <ids>] [--exclude <ids>] [--receipt-dir <path>]]` | Zendesk bulk delete: sign in, search, preview; with `--dry-run` shows the requests, with `--delete` deletes the selection after two typed confirmations and writes a receipt. | Read-only by default. **`--delete` deletes Zendesk tickets** (recoverable for 30 days); browser sign-in, interactive only. |
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

**Manual and automatic runs.** A resource that Acronis reports as running is shown as In progress, and a ticket drafted mid-backup would differ from one drafted afterwards. How the review treats that depends on how it was started:

- **Manual** (every run today: the console, the interactive shell and `serve`): one pass, no waiting. Running resources are still reported honestly. A running backup that already has a successful backup is counted as healthy, but the console, the web page and the ticket all say how many resources were still in progress at review time and that their result is not final. A review with nothing else to report is titled "backups still in progress", never "no attention required".
- **Automatic** (for a future unattended run; nothing starts one yet): waits for running resources and re-reviews only the tenants that still have some. It prints how many are still running and how long it has waited. A tenant whose re-review fails is a failed tenant review like any other. When the maximum wait ends, the remaining resources stay In progress, are counted as pending in the report, console and ticket, and are never shown as healthy. With nothing running there is no wait and no extra call.

The host chooses the mode when it builds the workflow (`ReviewRunMode`); there is no command-line switch. The wait settings below apply to automatic runs only and are per profile and module:

```json
"review": {
  "backup-aggregator": {
    "inProgressMaxWaitSeconds": 1800,
    "inProgressPollIntervalSeconds": 30
  }
}
```

Both values are optional and default to 1800 and 30. `0` for the maximum wait disables waiting even for automatic runs. The single-tenant `inventory-review <tenant>` never waits. `configure status` shows the effective values.

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

## Usage report

```cmd
SupportToolkit usage-report [--names]
```

Every month, finance compares each customer's Acronis storage with what Business Central bills for backup. The usage report does that comparison: one row per customer with an active backup subscription, the billed quantity and amount next to Acronis's storage, and the difference. It is also a page in the [Web UI](#web-ui) (**Generate report**). It only reads, and nothing is saved: the report is kept in memory until the next run or until the program exits.

**What is read.**

- **Acronis**, per customer tenant: the three figures in the portal's storage view. Total storage is `storage_total`, Backup storage is `total_storage`, and Microsoft 365 and Google Workspace backup is `c2c_storage`. Acronis splits a metric into lines by edition and storage location; they are added up.
- **Business Central**: the subscription lines for the backup items, with item, description, unit, quantity, unit price, discount and amount. The amount is Business Central's own figure; the toolkit calculates no prices. These columns are read by a separate request with its own field list, so the entitlement read never asks for prices and a renamed price column fails only this read.
- Customers are matched to Acronis tenants by name, automatically, as in the monitored review. Nothing is stored.

**Units.** Usage is compared in decimal GB (1 GB = 1,000,000,000 bytes), the unit the backup quantity is billed in. The Acronis portal labels its figures GB but counts 1,073,741,824 bytes per GB, so for the same data it shows about 7% less. Each row's details show the figures as the portal shows them; the detailed view adds the compared decimal GB and the bytes.

**What the report computes.** Only a unit conversion, adding up (a metric's lines, a customer's tenants, a customer's backup lines) and one subtraction: billed GB minus the Acronis total. Nothing is judged and there are no thresholds. A customer whose Acronis total is not backup + Microsoft 365 gets a note; no figure is changed.

**Rows and lists.**

- **The report:** customers with at least one active, not omitted backup line. A row is *unknown* when a quantity or amount is missing, no tenant is matched (or the match needs review), or Acronis returned something unusable, such as a missing value or a unit other than bytes.
- **Set up, no usage reported yet:** an active backup line and a matched tenant, but Acronis has reported nothing. Listed apart, to raise with whoever set it up; not compared.
- **Not active:** every backup line is on a subscription that is not active, or omitted. Left out of the report; the detailed view lists them, with any Acronis storage.
- **Storage without a backup subscription:** customer tenants with storage that no customer with a backup line was matched to.
- **Special cases:** tenants that fit none of the above but are worth knowing about, such as a tenant with no matched subscription and no usage reported, or one with no storage at all. The detailed view lists them; the simple view gives the count.

Missing or unusable data is unknown with a reason, never zero.

**Caveat.** Acronis describes these usage figures as refreshed every few hours and not meant for billing; the report's notes (in the detailed view, and in the console summary) say so. Check a few customers against the portal and Business Central before relying on it.

**Configuration**, per profile. The item number below is fictional:

```json
"modules": { "usage-report": "production" },
"monitoringScope": { "usage-report": { "serviceItemNumbers": [ "EXAMPLE-BACKUP-ITEM" ] } }
```

Production mode needs the profile's Acronis and Business Central connections and at least one backup item; `configure status` shows the mode and items. Fixture mode needs nothing and contacts nothing.

The console prints counts by default. `--names` adds the per-customer rows, which contain customer names and amounts. Exit codes: `0` complete, `2` generated but incomplete, `1` not available or invalid arguments.

**Design.** The module is provider-agnostic: its rules see only its own types. Everything specific to Acronis and Business Central (metric names, unit checks, field mapping) lives in `Modules/UsageReport/Production`, and `LayeringTests` fails if anything else in the module references a provider.

---

## Zendesk bulk delete

SupportToolkit has an interactive way to find and bulk-delete Zendesk tickets, for example everything for a customer that has left. It signs in, searches, shows a preview, and, only when asked with `--delete`, deletes the tickets you selected after two typed confirmations and writes a receipt. Without `--delete` it only reads. It is available as a developer command and as a page in the [Web UI](#web-ui), which follows the same rules.

### Two Zendesk connections

SupportToolkit has two Zendesk connections, separated by function. They are configured, resolved and reported independently, and share no credentials, scope, client or token.

| | Ticketing | Deletion |
|---|---|---|
| Purpose | Create private tickets from backup reviews | Interactive search and bulk delete |
| OAuth client | Confidential, client credentials | Public, PKCE |
| Secret | Yes, in the credential store | None: never asked for or stored |
| Scope | `tickets:write` | `read tickets:write` |
| Sign-in | Unattended-capable | Browser, operator present |
| Configuration | `connections.zendesk`, profile `zendeskConnection` and `zendeskDestination` | `connections.zendeskDeletion`, profile `zendeskDeletionConnection` |
| Environment overrides | `ZENDESK_*` | None |

Both may point at the same Zendesk subdomain; they still use different OAuth clients. A missing deletion connection does not affect ticketing, and the reverse. `configure status` shows them in separate sections.

### Sign-in

The deletion connection is a public OAuth client with PKCE (S256); no client secret exists. The sign-in opens your browser, listens on a free loopback port and sends `http://localhost:{port}` as the redirect URI (the OAuth client registers `http://localhost`; Zendesk ignores the port). It requests `read tickets:write`: `tickets:read` alone is not enough, because search and the signed-in user's identity need `read`. The token is kept in memory only and is gone when the program exits. If Zendesk reports no token lifetime, none is assumed; a rejected or expired token is not replaced silently and needs a new sign-in.

### Preview

```cmd
dotnet run --project src\SupportToolkit -- dev zendesk-bulk-delete --production --organization "Example Org"
```

At least one search key is required, because an empty search would select every ticket. The command refuses to run with redirected input or output.

| Key | Matches | Zendesk query |
|---|---|---|
| `--organization <name>` | the ticket's organization | `organization:"name"` |
| `--sender <name>` | the requester by name, full or partial (the sender of an email ticket) | `requester:"name"` |
| `--requester <email>` | one requester by email address | `requester:address` |
| `--domain <domain>` | requesters at an email domain | `requester:*@domain` |
| `--text <text>` | free text, passed through as written (Zendesk operators such as `status:closed` work) | the text |

Keys are combined with AND, and every search is restricted to tickets. Zendesk documents `requester:` as taking a name or an email address, and says wildcards do not work for email address searches, so **`--domain` may return nothing** and is unverified; prefer `--sender`, `--requester` or `--organization`.

- **Search completeness.** Zendesk search returns at most 1,000 results, can repeat a ticket across pages, and reports a total count. Results are de-duplicated, and the reported count is compared with the unique tickets received. If they differ, or more than 1,000 match, the preview says **INCOMPLETE SEARCH** and exits with code 2. It is never presented as complete.
- **Exception-first.** Totals, status counts, and breakdowns by organization and requester domain (so strays stand out). Tickets that are not closed are listed as exceptions and not selected. Tickets closed less than 24 hours ago cannot be deleted through the API and are flagged; the closing time is approximated by the last update, and a missing time is treated as too recent. Closed tickets are selected by default.
- **Names.** Search results carry only organization and requester IDs, so names and domains come from two further read-only lookups. If a lookup fails the preview still appears, with those names marked unknown and a note saying so.

The preview contains customer-related names and ticket subjects. It is for your own console and must not be pasted into public places.

### Selection and deletion

```cmd
dotnet run --project src\SupportToolkit -- dev zendesk-bulk-delete --production --sender "Example Sender" --dry-run
dotnet run --project src\SupportToolkit -- dev zendesk-bulk-delete --production --sender "Example Sender" --delete
dotnet run --project src\SupportToolkit -- dev zendesk-bulk-delete --production --sender "Example Sender" --delete --include 12,15 --exclude 20
```

| Option | Meaning |
|---|---|
| `--dry-run` | Shows the exact requests that would be sent. Sends nothing and writes no receipt. |
| `--delete` | Deletes the selected tickets after two typed confirmations. |
| `--include <ids>` | Adds tickets to the selection, such as `12,15,20`. This is the only way to include a ticket that is not closed. |
| `--exclude <ids>` | Removes tickets from the selection. |
| `--receipt-dir <path>` | Where the receipt is written. Defaults to a `SupportToolkit\receipts` folder under your local application data. |

- **Selection.** Closed tickets are selected by default. A ticket closed less than 24 hours ago can never be selected, because the API cannot delete it. An ID that is not in the search result is an error, not ignored.
- **A frozen list.** Before anything is asked, the exact ticket IDs are printed. Only those IDs are ever sent: the search is never run again at delete time.
- **Two confirmations.** First you type the number of tickets to delete. If any selected ticket is not closed, they are listed and you type their count too. Any other answer cancels, and nothing is sent.
- **An incomplete search is never deleted from.** Narrow the search first.
- **How Zendesk deletes.** The request starts a background job for up to 100 tickets, and the job is followed to its end, one at a time. Zendesk reports success per ticket. There are no automatic retries.
- **Nothing unclear is called deleted.** A ticket is *Deleted* only when Zendesk's job says so. If a request or job ends unclearly (an error, a job that fails or does not finish) the ticket is *Unknown*, the run stops, the remaining tickets are *Not attempted*, and the exit code is `2`. For an *Unknown* ticket, check Zendesk's deleted tickets view.
- **Recovery.** Zendesk keeps deleted tickets for 30 days. Deleted archived tickets (closed for a long time) skip that view and can only be restored through the API by ID, which is why the receipt exists.

### Receipts

Before the first request, a receipt listing every ticket ID is written. If that cannot be written, nothing is deleted. It is rewritten when the run ends, including a run that stopped or was cancelled. It records the Zendesk instance, who signed in, the search, the time, the typed confirmations, and each ticket's ID, its status when planned, its outcome and its job ID. It never contains ticket subjects or content.

A receipt still identifies a customer's tickets, so the folder is refused if it lies inside a git working tree. Keep receipts private and do not share them publicly.

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

A Zendesk deletion connection is a separate kind of connection: it defines only the instance and a public OAuth client identifier, with no secret, destination or user IDs. See [Two Zendesk connections](#two-zendesk-connections).

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

**Importing a settings file.** A settings file that was handed over can be put in place without finding the folder:

```cmd
SupportToolkit configure import
SupportToolkit configure import <file>
```

Without a file it looks in the folder next to `SupportToolkit.exe`. A settings file is recognised by its content (JSON with at least one profile), not its name, and the files that ship with the program are ignored. It must be the only one there; if there are several, none is used. The file is moved, not copied, and moved as it is. If settings already exist they are kept beside them as `local.config.json.bak-<date>-<time>` and never overwritten. The release zip includes `Import config.cmd`, which runs this for you.

A settings file holds no passwords or client secrets. When the import finishes it lists the connections that a profile uses (Acronis and Zendesk ticketing have a client secret; the others sign in interactively) which have none stored on this computer, and asks for each: typing is hidden, and Enter skips one. Anything skipped can be entered later with `SupportToolkit configure`; `configure status` shows what is missing.
**Saving keeps settings it does not recognise.** If the file holds a setting this version has no property for (for example one written by a newer version), it is kept exactly as written and written back on the next save, such as after a profile switch. Only settings retired on purpose are dropped (see below). Versions from before this behaviour still discard unrecognised settings when they save, so back up `local.config.json` before running an older build against a configuration written by a newer one.

### Retired write switch

Earlier versions had a global `allowWrites` profile setting and a `SUPPORTTOOLKIT_ALLOW_WRITES` environment variable. Both are retired:

- Existing configuration files that still contain `allowWrites` load normally, and the property is dropped the next time the file is saved.
- `SUPPORTTOOLKIT_ALLOW_WRITES` is ignored. `configure status` reports it as obsolete if it is still set.

The switch was removed because it duplicated the safeguards that actually gate a write: an explicitly invoked submit operation, a configured Zendesk connection, a valid destination, and (in the interactive console) a confirmation prompt.

### Retired BackupHealth module

The `backup-health` command was retired because BackupAggregator covers the needed visibility. A saved profile that still has a `backup-health` entry under `modules` still loads; the entry is ignored and dropped the next time the configuration is saved. Provider connections, including the shared Acronis connection, are not affected. `SUPPORTTOOLKIT_BACKUP_HEALTH_MODE` is obsolete and ignored; `configure status` reminds you to remove it if it is set.

---

## Environment overrides

Runtime modes can be overridden independently of saved profile settings.

For example:

```cmd
set SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE=production
```

Supported values are `fixture` and `production`.

Clear an override in Windows CMD:

```cmd
set SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE=
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

This command uses the active profile's configured Acronis connection and does not require changing BackupAggregator to production mode.

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
- Profile switching
- Configuration wizard
- Debug logging toggle

The BackupAggregator menu offers the monitored review (the tenants Business Central entitles, plus the operator's own), ticket preview, and destination-aware ticket submission from that review. It needs a Business Central connection in the active profile, or fixture mode. The all-tenant review and its ticket are not in the menu; they remain available as `inventory-review` and `ticket preview` / `ticket submit` on the command line for development.

Interactive and raw CLI operations share the same application workflows.

---

## Web UI

```cmd
SupportToolkit serve
```

The release zip also has `Start SupportToolkit.cmd`: double-click it to start the web UI without typing a command. Leave its window open while you work; press **Exit SupportToolkit** in the page, or close the window, when you are finished.

`serve` starts a local web UI in your browser. It is built into the same executable; nothing else is installed. Its pages are:

- **Profile.** Choose which profile everything uses. The configuration wizard stays in the console (`SupportToolkit configure`).
- **Backup Aggregator.** Run or refresh the monitored review, with progress and Cancel. Read the completeness banner and the Attention, Unclassified and Incomplete tabs (the detailed view adds Scope and Reconciliation). **Prepare ticket** then shows the exact ticket and its Zendesk destination to confirm before anything is created. What can be prepared or submitted, and that a prepared ticket is used once, is decided by the same `BackupReviewSession` the console uses.
- **Usage report.** **Generate report** compares Acronis storage with Business Central billing per customer, with progress and Cancel. Each row folds out to show its billing lines and the Acronis figures in both units; the detailed view adds the portal's figures and the customers whose subscription is not active. See [Usage report](#usage-report).
- **Zendesk bulk delete.** Sign in, search, choose tickets, check the plan and type the confirmations, with progress and a result. The rules are the command's: closed tickets are chosen for you and others need a second confirmation, an incomplete search is never deleted from, and nothing is sent that was not in the confirmed plan. Deletion runs in the background, so reloading or closing the tab does not stop it; **Exit** stops it cleanly and writes the receipt.

A **Simple / Detailed** switch in the top bar (Simple by default) hides technical detail such as ticket ID lists, request counts, job progress and the scope tabs. It never hides a warning, the Zendesk instance, a count, a typed confirmation, a failed or unknown outcome, or the receipt.

**The mode is shown at the top of the page.** A profile in production mode reads real customer data, and customer names will appear on screen. Check the banner before sharing your screen.

**It is local only.**

- It listens on `127.0.0.1` on a free port and cannot be reached from the network. There is no option to listen elsewhere.
- Each run prints a link containing a random launch token. Opening it signs your browser in with a session cookie (`HttpOnly`, `SameSite=Strict`) and redirects to a clean URL. Without that cookie every request, including the page's live connection, is refused, so another website or program on the machine cannot use the UI.
- The `Host` header must be the loopback address and port (against DNS rebinding), and any request that changes state, or opens a WebSocket, must come from the UI's own origin.
- Nothing is loaded from another site, secrets are never sent to the browser (only whether one is present), and request URLs are not logged because they carry the token.

Stop it with the **Exit SupportToolkit** button, or Ctrl+C. Closing the browser tab alone does not stop it. The link works for that run only; start `serve` again for a new one.

Options: `--port <n>` to use a fixed port, and `--no-browser` to print the link without opening the browser.

**Limits.** One operator: two browser tabs show the same state. Secrets still come from Windows Credential Manager, so production use is Windows-only. The release zip holds `SupportToolkit.exe`, the two `.cmd` launchers, a `wwwroot` folder and a small manifest beside it (the web framework's script); keep them together, as with `Fixtures`.

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
    │   ├── Interactive/
    │   └── Web/ (Blazor Server pages, local-only host)
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
    │   ├── UsageReport/
    │   │   ├── Fixtures/
    │   │   ├── Models/
    │   │   ├── Production/   (the only part that knows Acronis and Business Central)
    │   │   ├── Reporting/
    │   │   └── Services/
    │   │
    │   └── ZendeskBulkDelete/
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

Acronis integrations are separated by responsibility, including tenant discovery, Microsoft 365 resources, and devices.

Zendesk implements the shared ticket-provider capability.

Business Central supplies subscription evidence and customer names through neutral Core contracts, so any module can scope itself by entitlement with its own item numbers.

### Core

Core contains shared configuration, secure credential storage, logging, error handling, and ticketing abstractions.

### CLI and UI

The raw CLI and interactive console are separate presentation layers over the same workflows.

### Keeping workflows interface-agnostic

Workflows and providers return models and take callbacks. They do not write to a console, and they do not know about the interfaces built on them. That lets another interface (for example a local web page) reuse them without changing them.

- **Progress** is a callback the host supplies to the workflow constructor. The console host passes `ConsoleProgress.Write`; a different host passes its own.
- **Whether a ticket can be created** is decided in one place, `BackupAggregatorWorkflow.PrepareScopedTicketDraft` and `PrepareScopedTicketSubmissionAsync`. They return a typed result (ready, or not ready with a kind and reasons) instead of throwing for expected conditions. A submission plan bundles the exact draft, the Zendesk destination and the review's numbers, and is what the operator confirms.
- **The review an operator is working with** is held by `BackupReviewSession`, not by the console menu. It owns the rules: nothing can be prepared before a review, a plan is valid only for the review it was prepared from, and a plan is submitted once. The console menu, and any other interface, are thin adapters over it.
- **A test enforces the layering** (`LayeringTests`): nothing in `Core`, `Providers` or `Modules` may use the console, reference the `Ui` or `Cli` layers, or reference a web or desktop UI framework. Console reporters (`Reporting/Console*.cs`) and a module's CLI command class (`Modules/<Name>/<Name>Module.cs`) are the adapters. Fix a failure by moving the console code up, not by extending the test's allowlist. A provider-agnostic module (currently the usage report) may reference `Providers` only in its `Production` folder, which maps provider data to the module's own types.

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

### Zendesk deletion

The deletion connection is a different connection from ticketing, with its own public OAuth client and no stored secret. It has two separate HTTP pipelines. The read-only one permits only GET requests to four allowlisted endpoints (search, the signed-in user, and user and organization lookups) and can never delete. The deletion pipeline is built only when `--delete` is used, and permits exactly two requests: `DELETE` of `destroy_many` with 1 to 100 distinct positive numeric ticket IDs and no other parameter, and `GET` of a job status. Every other method, path or host is blocked before a token is requested. Sign-in is interactive in the operator's browser on every run, and the token lives in memory only.

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
| `v0.4.0` | 2026-11-03 | Local web UI (`serve`, or double-click `Start SupportToolkit.cmd`): profile selection, the Backup Aggregator review and ticket, and Zendesk bulk delete with typed confirmations and receipts, with a Simple / Detailed view. `configure import` moves a handed-over settings file into place and asks for the secrets it needs. Saving now keeps settings the program does not recognise. The console and its commands are unchanged. | The console was hard to hand to colleagues. Both interfaces now sit on the same session rules, so they cannot diverge. |
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
- **Saving keeps unrecognised settings.** A setting the program has no property for is written back as it was, instead of being dropped on the next save. Only settings retired on purpose are removed.
- **Diagnostics are redacted.** UUIDs are redacted in operational logging, and debug output reports exception types and HTTP statuses rather than raw messages or stack traces.

## Development branches

One feature branch contains work that is **not** in `main` or in any release:

| Branch | Contents | Evidence boundary |
|---|---|---|
| `feature/compliance-investigation` | A synthetic Intune and Microsoft Graph compliance module: exception-first device list, setting-level evidence, device investigation and a consolidated multi-tenant `compliance report`. | Uses synthetic fixtures. No live Graph authorization, tenant coverage or case lifecycle automation has been established. |

Synthetic fixtures do not prove real API contracts, authorization or completeness.

## Project status

SupportToolkit is under active development.

BackupAggregator and Zendesk ticket submission are implemented on `main`, with automated test coverage and selected production integration validation:

- Acronis production reads and comparisons against the Acronis portal have been exercised for BackupAggregator and tenant discovery.
- Business Central reads (subscriptions and customer names) and the automatic matching were exercised with read-only commands against a live environment; the scoped review has had limited end-to-end validation.
- Zendesk ticket routing and idempotent replay were validated in a personal trial account. Authorized end-to-end validation in an organizational Zendesk environment has not been completed.
- The Zendesk bulk delete, including real deletion through the web UI, was exercised only in a personal sandbox account with a set of test tickets. It has not been used against an organizational Zendesk.
- The usage report has had one read-only production run, checked by eye against the Acronis portal for one customer. It has not been reconciled against finance's own monthly figures.

Provider behavior is only considered production-validated where real external responses have been observed and checked.

Deployment model, scheduling, persistence and an operator-friendly interface are intentionally undecided. Different modules have different operational needs, and the console has usability limits for non-developers. A local web UI now exists for the Backup Aggregator, Zendesk bulk delete and the usage report; it runs on the same rules as the console. These will be revisited when a validated workflow needs them rather than designed up front.

The current focus is correctness, maintainability, explicit operator control, and practical usefulness in MSP support workflows.

Sanitized design decisions, operational discoveries and validation notes are kept in the public [SupportToolkitNotes](https://github.com/YayaTheBehemoth/SupportToolkitNotes) repository. This repository's source and tests are authoritative for implementation.
