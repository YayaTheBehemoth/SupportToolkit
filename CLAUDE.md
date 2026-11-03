# SupportToolkit — Claude Code instructions

This is a modular .NET 10/C# MSP automation CLI. Implementation and tests in this repository are authoritative; sanitized design/requirements notes live at https://github.com/YayaTheBehemoth/SupportToolkitNotes.

## Engineering workflow

- Inspect the relevant module/provider/core implementation and tests before suggesting or making changes.
- Prefer focused, minimal local edits and fixture-driven regression tests. Preserve existing functionality and module boundaries.
- Use `dotnet test` for relevant checks and `dotnet run --project src/SupportToolkit -- <command>` for CLI development.
- Treat synthetic Graph and Business Central providers as development evidence, not production API validation, authorization or completeness.
- Preserve explicit unknown/failed/incomplete reporting; never turn missing or contradictory data into a success assertion.
- Treat external integrations as read-only unless a particular write is explicitly authorized. Do not run real production API calls or create tickets as part of a coding task without approval.
- Never include credentials, tokens, customer or tenant identifiers, private support tickets, real payloads, or internal investigative procedures in public code, tests, logs, documentation or AI prompts.
- Do not impose a universal hosting, scheduling or persistent-state architecture. A local, loopback-only web UI (`serve`, in `src/SupportToolkit/Ui/Web`) exists by the user's decision, as one thin interface beside the console, not as a hosting model. Keep the rules in modules and sessions (for example `BackupReviewSession`, `BulkDeleteReviewSession`) so the console and the web cannot diverge; `LayeringTests` enforces that nothing below `Ui` uses the console or a web/desktop framework. Module operational requirements and any other user interface are still under discovery.

## Git ownership

**The user owns the entire Git workflow for this code repository.** Do not create/switch/delete branches, stage, commit, push, merge, tag, publish releases, open PRs, or modify remote GitHub unless the user explicitly requests that specific action. A request such as “fix this” or “implement this” authorizes neither commit nor push. Local inspection and reporting are fine. After local changes, show what changed, suggested tests, and let the user decide all Git operations.

SupportToolkitNotes has a separate sanitized documentation workflow; permission there never grants Git permission here.

## Current snapshot (3 Nov 2026; verify before relying)

- Released `main`: `v0.4.0`, which adds the local web UI (`serve`: Profile, Backup Aggregator and Zendesk bulk delete pages, with a Simple/Detailed view), `configure import` with double-click launchers in the zip, and settings that survive a save. `v0.3.0` added the Business Central monitoring scope; `v0.2.4` and `v0.2.3` are earlier baselines.
- `feature/compliance-investigation`: fixture-driven compliance evidence and consolidated report; no production Graph authorization. It contains `main` as of `v0.4.0`.
- Business Central entitlement and tenant scoping shipped in `v0.3.0`; its live validation was limited and read-only.
- Zendesk ticket routing and the bulk delete (including real deletion) were tested only in a personal account or sandbox; company end-to-end integration needs separate authorized validation.

For current priorities and decisions consult SupportToolkitNotes, especially `Strategy Checkpoint - 2026-10-20.md`, `Architecture and Decisions.md`, and `Roadmap.md`. If Notes aren't mounted locally, request the relevant files or use an authorized connector rather than assuming they are available.
