[![Download for Windows] CLICK TO DOWNLOAD NEWEST RELEASE (https://img.shields.io/badge/Download-Windows_x64-blue?logo=windows)](https://github.com/YayaTheBehemoth/SupportToolkit/releases)


# SupportToolkit

SupportToolkit is a modular .NET console application for automating repetitive MSP IT support tasks.

It currently provides Acronis backup inventory and health monitoring workflows, with shared configuration, credential management, and Zendesk ticketing functionality.

The application supports both an interactive console and direct CLI commands.

## Features

| Component | Description |
|---|---|
| BackupAggregator | Reviews Acronis backup inventory across individual or multiple customer tenants and produces consolidated exception reports |
| BackupHealth | Evaluates Acronis backup health information and identifies resources requiring attention |
| Ticketing | Generates ticket drafts from BackupAggregator results and supports Zendesk ticket submission |
| Interactive CLI | Menu-driven access to operational workflows |
| Configuration | Persistent runtime profiles, provider connections, and credential management |

## Installation

### Portable Windows Installation

SupportToolkit can be distributed as a self-contained Windows x64 application.

The portable package does not require the .NET SDK or a separate .NET runtime installation.

1. Obtain `SupportToolkit-win-x64.zip`.
2. Extract the archive to a directory of your choice.
3. Run `SupportToolkit.exe`.
4. Complete the configuration wizard on first launch.

The archive includes the executable and the supporting files required by the application.

No installation wizard or administrator-level installation is required by SupportToolkit itself.

### First-Run Configuration

When no configuration profile exists, SupportToolkit automatically launches its configuration wizard.

The wizard allows the operator to configure:

- Runtime profile name
- Fixture or production execution mode for each module
- Acronis API connection
- Optional Zendesk connection
- External-write permissions

Production connections require the relevant API credentials.

Fixture mode can be used without connecting to external services.

After saving the configuration, SupportToolkit opens its interactive menu.

### Configuration Storage

Portable releases store non-secret configuration at:

```text
%LOCALAPPDATA%\SupportToolkit\local.config.json
```

Authentication secrets are stored separately using Windows Credential Manager.

Configuration remains available between application launches.

To inspect the current configuration:

```cmd
SupportToolkit.exe configure status
```

To modify it:

```cmd
SupportToolkit.exe configure
```

To select a different profile:

```cmd
SupportToolkit.exe configure use profile-name
```

### Updating the Portable Application

To update an existing portable installation:

1. Obtain a newer distribution archive.
2. Extract it to the desired application directory.
3. Launch the updated executable.

The default configuration and stored credentials are maintained separately from the application directory, so replacing the executable does not remove them.

---

## Building from Source

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

Run tests:

```cmd
dotnet test
```

Launch the application:

```cmd
dotnet run --project src\SupportToolkit
```

Running without arguments starts the interactive console.

### Development Configuration

Debug builds use `local.config.json` at the repository root.

Release builds use `%LOCALAPPDATA%\SupportToolkit`.

An explicit `SUPPORTTOOLKIT_CONFIG` environment variable overrides the default location.

To start configuration manually:

```cmd
dotnet run --project src\SupportToolkit -- configure
```

Additional commands:

```cmd
dotnet run --project src\SupportToolkit -- configure status
dotnet run --project src\SupportToolkit -- configure profile local-dev
dotnet run --project src\SupportToolkit -- configure use local-dev
dotnet run --project src\SupportToolkit -- configure profile delete old-profile
```

### Building a Portable Distribution

From the repository root:

```cmd
scripts\publish-windows.cmd
```

The publishing script:

1. Builds the project in Release configuration.
2. Runs the automated tests.
3. Publishes a self-contained Windows x64 executable.
4. Packages the published files into a ZIP archive.

Generated output:

```text
artifacts/
├── publish/
│   └── win-x64/
│       └── SupportToolkit.exe
│
└── SupportToolkit-win-x64.zip
```

The publish directory also contains the supporting fixture files.

The generated ZIP can be extracted and run independently of the source repository.

---

## BackupAggregator

BackupAggregator retrieves Acronis backup inventory data and generates consolidated reports across one or more customer tenants.

It summarizes healthy resources and highlights exceptions rather than displaying every backup resource individually.

### Functionality

- Discover Acronis customer tenants
- Review individual tenants or all discovered customer tenants
- Retrieve Microsoft 365 and device backup inventory
- Handle paginated API responses
- Normalize and deduplicate resource data
- Classify backup resources
- Aggregate resource and tenant statistics
- Suppress healthy resources from detailed output
- Report resources requiring attention
- Identify incomplete tenant reviews

### Resource Classification

| Classification | Description |
|---|---|
| Healthy | Available data provides sufficient evidence of a healthy backup state |
| NeedsReview | The resource reports a condition requiring attention |
| Unknown | Available data is insufficient for a reliable classification |

Every normalized resource is accounted for:

```text
Resources checked = Healthy + NeedsReview + Unknown
```

Tenant reviews are accounted for separately, including failed reviews.

### Inventory Coverage

The currently implemented inventory collection uses two Acronis data sources:

**Microsoft 365:** Resources retrieved through Acronis Resource Manager endpoints.

**Devices:** Resources retrieved through the built-in `AllMachines` inventory.

Resource counts represent Acronis resource objects, which do not necessarily correspond one-to-one with physical devices.

Coverage is limited to the inventory sources currently implemented and validated.

### Commands

The following commands use the source-development CLI. For portable installations, replace `dotnet run --project src\SupportToolkit --` with `SupportToolkit.exe`.

Review a single tenant:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator inventory-review "Customer Example"
```

Review all customer tenants:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator inventory-review --all
```

Run the fixture workflow:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator
```

The last command requires BackupAggregator to be configured for fixture mode.

### Exit Codes

| Code | Description |
|---|---|
| 0 | Review completed without tenant-level failures |
| 1 | Invalid command or execution error |
| 2 | One or more tenant reviews were incomplete |

Backup exceptions do not themselves cause an execution failure.

---

## BackupHealth

BackupHealth retrieves Acronis backup health information and evaluates resources against a set of health rules.

The workflow:

1. Retrieves resource and diagnostic information.
2. Builds a normalized backup health snapshot.
3. Evaluates resources for exceptions.
4. Presents the results in the console.

The current exception evaluation includes detection of stale backup activity using a 48-hour threshold.

### Command

```cmd
dotnet run --project src\SupportToolkit -- backup-health
```

BackupHealth supports both fixture and production execution.

---

## Ticketing

SupportToolkit includes shared ticketing functionality, currently integrated with BackupAggregator.

The integration supports:

- Generating ticket drafts from backup review results
- Previewing generated tickets
- Submitting tickets through Zendesk

### Commands

Preview a ticket:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator ticket preview
```

Submit a ticket:

```cmd
dotnet run --project src\SupportToolkit -- backup-aggregator ticket submit
```

Ticket submission requires a configured Zendesk connection and explicitly enabled external-write permission.

The interactive console additionally requests confirmation before submission.

---

## Interactive Console

Running SupportToolkit without arguments opens the interactive interface.

The main menu provides access to:

- BackupAggregator
- BackupHealth
- Runtime profile selection

The BackupAggregator interface allows the operator to run tenant reviews, view reports, preview tickets, and submit tickets.

The interactive interface and direct CLI use the same underlying application workflows.

---

## Configuration

SupportToolkit uses named configuration profiles to control runtime behavior.

Profiles specify:

- Module execution modes
- Acronis connection references
- Zendesk connection references
- External-write permissions

Provider connection metadata is stored separately from authentication secrets.

### Runtime Overrides

Module execution modes can be overridden through environment variables.

Windows CMD:

```cmd
set SUPPORTTOOLKIT_BACKUP_AGGREGATOR_MODE=fixture
set SUPPORTTOOLKIT_BACKUP_HEALTH_MODE=fixture
```

Valid modes are `fixture` and `production`.

### Debug Logging

Enable detailed diagnostics:

```cmd
set SUPPORTTOOLKIT_DEBUG=true
```

Disable:

```cmd
set SUPPORTTOOLKIT_DEBUG=
```

---

## Architecture

SupportToolkit separates application workflows, external integrations, and user interfaces.

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
    │   ├── Ticketing/
    │   ├── Logging/
    │   ├── ErrorHandling/
    │   └── Modules/
    │
    ├── Modules/
    │   ├── BackupAggregator/
    │   │   ├── Fixtures/
    │   │   ├── Models/
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
        └── Zendesk/

tests/
└── SupportToolkit.Tests/

scripts/
└── publish-windows.cmd
```

**Modules** contain operational workflows, domain models, services, and reporting.

**Providers** handle communication with external systems. The Acronis integration uses separate providers for tenant discovery, Microsoft 365 resources, devices, resource status, and alerts.

**Core** contains shared configuration, credential storage, logging, module contracts, and ticketing abstractions.

**CLI and UI** provide separate entry points to the same application workflows.

---

## Security

- **Acronis:** API communication uses a restricted HTTP transport permitting approved read operations and required authentication requests.
- **Credentials:** Authentication secrets are stored using Windows Credential Manager rather than plain-text configuration files.
- **External writes:** Disabled by default and explicitly required for Zendesk ticket submission.
- **Test data:** Fixture data supports local development and testing without accessing production customer environments.

Production credentials and unsanitized customer data should not be committed to the repository.

---

## Testing

Run the test suite:

```cmd
dotnet test
```

Tests cover:

- Backup resource classification
- Microsoft 365 and device inventory collection
- Pagination and deduplication
- Tenant selection and resolution
- Multi-tenant aggregation
- Failure isolation and resource accounting
- Console reporting
- Provider transport restrictions
- Runtime configuration and workflow behavior
