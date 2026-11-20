# Duplicati lab

Two throwaway Duplicati servers for **API contract discovery** only. One
Duplicati server backs up one machine, so `machine-a` and `machine-b` stand
in for two customer machines. Daily development and every test run against
recorded, synthetic fixtures in `tests/`, never against these servers.

## Start and stop

Set the secrets first (Codespaces secrets, or your shell). There are no
defaults.

```sh
export DUPLICATI_LAB_SETTINGS_KEY=...   # any random value
export DUPLICATI_LAB_PASSWORD_A=...     # web/API password of machine-a
export DUPLICATI_LAB_PASSWORD_B=...     # web/API password of machine-b
docker compose -f lab/duplicati/compose.yml up -d
```

`machine-a` answers on `http://127.0.0.1:8201/`, `machine-b` on
`http://127.0.0.1:8202/`. Both back up the synthetic files in `sample/` to
folders inside their own volume. `docker compose -f lab/duplicati/compose.yml
down -v` removes them and their data.

## What the API does (Duplicati 2.4.1.0, observed November 20, 2026)

**Sign-in.** `POST /api/v1/auth/login` with `{"Password": ..., "RememberMe": false}`
returns `{"AccessToken": ...}` (a bearer token) and sets a refresh cookie
scoped to `/api/v1/auth/refresh`. A wrong password is `401`
`{"Error":"Failed to log in"}`; any API call without a token is `401`.
Every server has its own password.

**Reading.**

| Request | What it gives |
|---|---|
| `GET /api/v1/backups` | Every job: `ID`, `Name`, `TargetURL`, `Metadata` |
| `GET /api/v1/backup/{id}` | One job |
| `GET /api/v1/backup/{id}/log?pagesize=N` | The job's runs, newest first; `Message` is a JSON string with `ParsedResult` (`Success`, `Warning`, `Error`, `Fatal`), `BeginTime`, `EndTime`, `Errors`, `Warnings` |
| `GET /api/v1/serverstate` | `ActiveTask` (the job running now, or null), `HasWarning`, `HasError` |
| `GET /api/v1/systeminfo` | `ServerVersion`, `MachineName`, `ServerTime` |

**Findings that shape a provider.**

- **`TargetURL` can hold destination credentials** in real configurations
  (for example `auth-password=` in S3 or SFTP URLs). It must never be read
  into the toolkit, logged or shown.
- **The last error survives later successes.** `LastErrorDate` and
  `LastErrorMessage` stay in the metadata after the job succeeds again; the
  latest outcome is whichever of the error and the last finished run is
  newer. Metadata times have whole-second precision (`20261120T115203Z`).
- **A warning shows only in the log.** A run with warnings has the same
  metadata as a success; its log entry says `ParsedResult: Warning`.
- **A run that fails before the job's local database exists leaves no log
  entry**, only `LastErrorDate` and `LastErrorMessage`.
- **`LastBackupDate` is the newest backup version, not the last run.** A run
  that finds no changes leaves it unchanged; `LastBackupFinished` moves.
- **A job that never ran** has empty metadata and no log.
- **Error messages contain paths**, which in real use can name hosts or
  users, so they are not copied into reports or tickets.
- A missing local destination folder is created automatically; a missing
  source fails the run (`No valid source folders found`), and one missing
  source among several is a warning.
