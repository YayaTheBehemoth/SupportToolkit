# SupportToolkit

SupportToolkit is a modular internal-support automation toolkit.

The current module, **BackupHealth**, is a prototype for exception-based Acronis backup reporting across multiple tenants.

Instead of manually reviewing healthy backup states, the goal is to aggregate Acronis data, suppress healthy resources, and surface only resources that require human attention.

## Current status

The BackupHealth vertical slice is implemented and runs end-to-end against synthetic, production-shaped Acronis fixtures.

Currently implemented:

- Acronis client-credentials authentication
- Bearer token handling and caching
- Tenant discovery
- Resource-status retrieval
- Alert retrieval
- Cursor-based pagination
- Fixture and HTTP provider implementations
- Tenant/resource/alert correlation
- BackupHealth domain normalization
- Exception evaluation
- Time-based stale-backup detection
- Console exception reporting
- Fixture and production runtime modes
- Graceful application error handling
- Automated tests

The HTTP integration has **not yet been validated against the organization's production Acronis environment**, because API credentials are not currently available.

## Architecture

```text
                         IAcronisProvider
                                |
                  +-------------+-------------+
                  |                           |
       FixtureAcronisProvider       HttpAcronisProvider
                  |                           |
          Synthetic JSON                Acronis API
                  |                           |
                  +-------------+-------------+
                                |
                       BackupHealthService
                                |
                    Normalized domain models
                                |
                     BackupExceptionEngine
                                |
                   ConsoleBackupHealthReporter
```

### Provider boundary

`IAcronisProvider` isolates the rest of SupportToolkit from Acronis-specific transport concerns such as:

- HTTP
- Authentication
- Endpoint paths
- Pagination
- Response deserialization

This allows the same BackupHealth logic to run against either synthetic fixture data or the real Acronis API.

### BackupHealth

`BackupHealthService` correlates:

- Tenants
- Resource protection status
- Active alerts

It maps external Acronis DTOs into SupportToolkit domain models.

`BackupExceptionEngine` evaluates those normalized resources and suppresses resources without actionable conditions.

## Project structure

```text
src/
└── SupportToolkit/
    ├── Core/
    │   ├── Configuration/
    │   └── ErrorHandling/
    ├── Modules/
    │   └── BackupHealth/
    │       └── Models/
    ├── Providers/
    │   └── Acronis/
    │       └── Dtos/
    ├── Reporting/
    └── Program.cs

tests/
└── SupportToolkit.Tests/
    ├── Core/
    ├── Modules/
    │   └── BackupHealth/
    └── Providers/
        └── Acronis/

Fixtures/
└── Acronis/
```

## Running locally

### Requirements

- .NET 10 SDK

Run all tests:

```cmd
dotnet test
```

Run SupportToolkit:

```cmd
dotnet run --project src\SupportToolkit
```

Fixture mode is the default and does not contact external systems.

Example output:

```text
SupportToolkit
Mode: Fixture

SupportToolkit - Backup Health
==============================

Resources scanned:    4
Healthy suppressed:   1
Exceptions:           3
  Critical:           1
  Warning:            2
```

## Runtime modes

SupportToolkit currently supports two runtime modes.

### Fixture mode

Fixture mode is the default.

It uses deterministic synthetic Acronis data from the `Fixtures/Acronis` directory and a fixed clock so development, testing, and demonstrations remain repeatable.

No external API calls are made.

Run normally:

```cmd
dotnet run --project src\SupportToolkit
```

### Production mode

Production mode must be explicitly enabled:

```cmd
set SUPPORTTOOLKIT_MODE=production
```

The following environment variables are required:

```text
ACRONIS_DATACENTER_URL
ACRONIS_CLIENT_ID
ACRONIS_CLIENT_SECRET
```

Credentials are intentionally supplied at runtime and must not be committed to source control.

If production mode is enabled without the required configuration, SupportToolkit fails immediately with an operator-friendly error.

Example:

```text
SupportToolkit
Mode: Production

ERROR: Required environment variable 'ACRONIS_DATACENTER_URL' is not configured.
```

The current production integration performs read-only API operations.

## Debugging

Normal application failures are displayed as concise operator-facing messages.

To include full exception details and stack traces:

```cmd
set SUPPORTTOOLKIT_DEBUG=true
```

Disable debug mode again with:

```cmd
set SUPPORTTOOLKIT_DEBUG=
```

## Acronis integration

The production provider is implemented against Acronis's documented APIs.

The current flow is:

```text
API client ID + secret
        |
        v
Client-credentials authentication
        |
        v
Bearer token
        |
        v
Root tenant discovery
        |
        v
Tenant / resource / alert retrieval
        |
        v
Cursor pagination
        |
        v
Normalized BackupHealth domain data
```

Transport and authentication concerns are isolated from BackupHealth through `IAcronisProvider`.

This means BackupHealth does not need to know whether its data came from:

```text
FixtureAcronisProvider
```

or:

```text
HttpAcronisProvider
```

## Exception-based reporting

The purpose of BackupHealth is to reduce routine manual review.

Instead of reporting every protected resource, SupportToolkit evaluates normalized backup state and surfaces only exceptions.

Current signals include:

- Acronis resource status
- Active warning or critical alerts
- Absence of protection policies
- Age of the last successful backup

Every surfaced exception includes the reasons it was reported.

Example:

```text
Customer Beta
-------------

[CRITICAL] FILES-01
  Last successful backup: 2026-09-27 03:00:00Z
  - Acronis reports the resource status as error.
  - Critical Acronis alert: BackupFailed.
  - Last successful backup is 4.4 days old.
```

Resources without actionable conditions are suppressed from the report.

## Testing

The project currently includes automated coverage for:

- Acronis DTO contract deserialization
- Fixture provider behavior
- Acronis authentication flow
- Bearer-token handling
- HTTP request construction
- Cursor pagination
- Runtime configuration
- Tenant/resource/alert correlation
- BackupHealth domain normalization
- Exception evaluation
- Time-based rules
- Graceful error formatting

Run the full suite with:

```cmd
dotnet test
```

## Current limitations

The production Acronis integration has not yet been validated against the organization's real Acronis environment.

The current HTTP implementation is based on:

- Acronis API documentation
- Documented response contracts
- Synthetic production-shaped fixtures
- Mocked HTTP responses

Some assumptions may need adjustment once real API responses are available.

Production validation should specifically confirm:

- Tenant hierarchy behavior
- Resource types present in the real environment
- Identifier relationships across Acronis APIs
- Alert payload shapes
- Pagination behavior at real scale
- Backup schedule semantics
- Appropriate stale-backup thresholds

The current fixed stale-backup threshold is intentionally temporary and should eventually be replaced with plan-aware scheduling logic.

## Next milestone

The next major milestone is validation against read-only production Acronis API access.

Once real responses are available, the intended workflow is:

```text
Real API response
        |
        v
Inspect and validate assumptions
        |
        v
Sanitize customer-specific information
        |
        v
Add regression fixture
        |
        v
Update DTO/provider/normalization behavior if required
        |
        v
Retain automated regression coverage
```

This allows discoveries made during production validation to improve the synthetic development environment rather than becoming undocumented one-off fixes.

## Future modules

BackupHealth is intended to be the first SupportToolkit module rather than a standalone Acronis application.

Potential future modules may include:

```text
Modules/
├── BackupHealth/
├── Compliance/
├── StaleDevices/
└── DeviceHealth/
```

Shared provider infrastructure can then support multiple modules without duplicating integration logic.

