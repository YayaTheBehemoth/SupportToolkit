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
- Do not impose a universal web, desktop, hosting, scheduling or persistent-state architecture. Module operational requirements and future user interfaces are still under discovery.

## Git ownership

**The user owns the entire Git workflow for this code repository.** Do not create/switch/delete branches, stage, commit, push, merge, tag, publish releases, open PRs, or modify remote GitHub unless the user explicitly requests that specific action. A request such as “fix this” or “implement this” authorizes neither commit nor push. Local inspection and reporting are fine. After local changes, show what changed, suggested tests, and let the user decide all Git operations.

SupportToolkitNotes has a separate sanitized documentation workflow; permission there never grants Git permission here.

## Current snapshot (21 Oct 2026; verify before relying)

- Released `main`: `v0.2.4`, including a Teams/SharePoint workload accounting correction in BackupAggregator; `v0.2.3` was the earlier core stabilization baseline.
- `feature/compliance-investigation`: fixture-driven compliance evidence and consolidated report; no production Graph authorization.
- `feature/business-central-monitoring-scope`: fixture-driven entitlement and tenant scoping; no production BC acquisition or identity mapping.
- Zendesk trial routing was tested in a personal account; company end-to-end integration needs separate authorized validation.

For current priorities and decisions consult SupportToolkitNotes, especially `Strategy Checkpoint - 2026-10-20.md`, `Architecture and Decisions.md`, and `Roadmap.md`. If Notes aren't mounted locally, request the relevant files or use an authorized connector rather than assuming they are available.
