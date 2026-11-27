# SupportToolkit — Claude Code instructions

This is a modular .NET 10/C# MSP automation CLI. Implementation and tests in this repository are authoritative; sanitized design/requirements notes live at https://github.com/YayaTheBehemoth/SupportToolkitNotes.

## Engineering workflow

- Inspect the relevant module/provider/core implementation and tests before suggesting or making changes.
- Prefer focused, minimal local edits and fixture-driven regression tests. Preserve existing functionality and module boundaries.
- Use `dotnet test` for relevant checks and `dotnet run --project src/SupportToolkit -- <command>` for CLI development.
- The project builds and tests on Windows and Linux (the dev container in `.devcontainer/` is the Linux reference). Windows-only code, such as the Windows Credential Manager secret store, is selected in `Composition/`; keep anything else platform-neutral.
- Treat synthetic Graph and Business Central providers as development evidence, not production API validation, authorization or completeness.
- Preserve explicit unknown/failed/incomplete reporting; never turn missing or contradictory data into a success assertion.
- Treat external integrations as read-only unless a particular write is explicitly authorized. Do not run real production API calls or create tickets as part of a coding task without approval.
- Never include credentials, tokens, customer or tenant identifiers, private support tickets, real payloads, or internal investigative procedures in public code, tests, logs, documentation or AI prompts.
- Layout (enforced by `LayeringTests`): modules are provider-agnostic and receive their sources through constructors; only a module's `Production/` (and the Backup Aggregator's Acronis fakes in `Fixtures/`) may reference `Providers`. `Composition/` reads the mode per run and builds the workflows; nothing else branches on fixture/production. `Domain/` holds shared commercial rules (tenants, customer matching, monitoring scope, reconciliation) and uses only `Core`; `Core` references neither `Domain` nor `Providers`. A provider's own conventions (kind names, ID formats) stay in its mapping to Domain types, and each transport is built only in its `*HttpPipeline.cs`.
- Providers and capabilities: a new provider owns its connection settings (`IProviderConnections`, read with `ProviderSettings` from its own `connections.<section>`) and is registered in `Composition/`, once in `ProviderConnectionsComposition` (a test fails if it is not) and once in its capability's registry (`TicketingComposition`, `BackupSystemsComposition`, `CommercialSystemsComposition`). Profiles select a system per capability (`ticketing`, `backup`, `commercial`); Core names only the first providers, for the older profile fields. A module gets each system only through the profile's capability selection; modules that share a capability never choose differently (bulk delete follows `ticketing`). Access stays separate per purpose: deleting never uses the credentials that create tickets, and must point at the selected instance. An operation the selected system cannot do is reported as unavailable, never redirected to another system.
- Do not impose a universal hosting, scheduling or persistent-state architecture. A local, loopback-only web UI (`serve`, in `src/SupportToolkit/Ui/Web`) exists by the user's decision, as one thin interface beside the console, not as a hosting model. Keep the rules in modules and sessions (for example `BackupReviewSession`, `BulkDeleteReviewSession`) so the console and the web cannot diverge; `LayeringTests` enforces that nothing below `Ui` uses the console or a web/desktop framework. Module operational requirements and any other user interface are still under discovery.

## Git ownership

**The user owns the entire Git workflow for this code repository.** Do not create/switch/delete branches, stage, commit, push, merge, tag, publish releases, open PRs, or modify remote GitHub unless the user explicitly requests that specific action. A request such as “fix this” or “implement this” authorizes neither commit nor push. Local inspection and reporting are fine. After local changes, show what changed, suggested tests, and let the user decide all Git operations.

- **Ask, don't assume.** Claude Code may ask for one specific Git operation, giving the exact command and a one-line reason, and runs it only after an explicit yes for that operation. An approval covers that one operation only; it does not carry over to later operations or to another session.
- Merge, rebase, force-push, tag, release, PRs, pushes to `main` and changes to `.github` happen only when the user requests them, never on Claude Code's own suggestion.

SupportToolkitNotes has a separate sanitized documentation workflow; permission there never grants Git permission here.

## Current state

- Derive the current state at the start of a session from Git (`git describe --tags`, `git branch -a`, recent `git log`) and from the code itself. Never trust versions, release contents or status written in any document, including this one.
- For priorities and decisions, read the **newest** `Strategy Checkpoint - *.md` in SupportToolkitNotes, then `Architecture and Decisions.md` and `Roadmap.md`. If Notes aren't mounted locally, request the relevant files or use an authorized connector rather than assuming they are available.

## Collaboration

- Challenge plans, handoffs and requests when the source or evidence points to something better. Propose the alternative with reasons and let the user decide. Agreed refinements replace the original plan; record them. Never deviate silently.

## Keeping this file current

- When a session changes a stable rule (layering, Git ownership, safety, workflow, platform support), propose an edit to this file at the end of the session.
- Status, versions, priorities and direction never go in this file.
