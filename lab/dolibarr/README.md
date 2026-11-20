# Dolibarr lab

A throwaway Dolibarr ERP/CRM for **API contract discovery** only: customers,
services and contracts standing in for an MSP's commercial records. Daily
development and every test run against recorded, synthetic fixtures in
`tests/`, never against this instance.

## Start and stop

Set the secrets first (Codespaces secrets, or your shell). There are no
defaults.

```sh
export DOLIBARR_LAB_DB_PASSWORD=...     # any random value
export DOLIBARR_LAB_ADMIN_PASSWORD=...  # web login 'labadmin'
docker compose -f lab/dolibarr/compose.yml up -d
```

Dolibarr answers on `http://127.0.0.1:8090/`; its REST API is under
`/api/index.php/`. The API, customers, contracts and products/services
modules are enabled at installation. `docker compose -f
lab/dolibarr/compose.yml down -v` removes it and its data.

An API key: `POST /api/index.php/login` with `{"login": ..., "password":
...}` returns `success.token`, or set one on the user in the web interface.
The key goes in the `DOLAPIKEY` header.

## What the API does (Dolibarr 23.0.4, observed November 20, 2026)

| Request | What it gives |
|---|---|
| `GET /contracts?limit=N&page=P&sortfield=t.rowid` | Contracts with their lines; `page` starts at 0; a page past the end is `[]` with `200` |
| `GET /thirdparties?mode=1&limit=N&page=P` | Customers: `id`, `name`, `code_client`, `status` |
| Wrong or missing key | `401` with a JSON error and a `debug` block (not to be logged) |

**Contracts and lines.**

- A contract has a `ref` (`CT2601-0001`), its customer's internal ID
  (`socid`) and a status: `0` draft, `1` validated, `2` closed.
- **Activity is per line, not per contract.** Each service line has its
  own status: `0` never activated, `4` running, `5` closed. A running line
  past its planned end is what Dolibarr shows as expired.
- A line names its service by `product_ref`, has a `qty`, the planned
  `date_start` / `date_end` and the actual `date_start_real` (activation)
  and `date_end_real` (closing).
- The customer number operators know is the third party's `code_client`
  (`CU2611-00001`); contracts only carry `socid`.

**Types are loose.** IDs, statuses and quantities are strings (`"4"`,
`"250"`); dates are Unix seconds, or `""`, or `null`, for the same field.
A reader must accept all three for a date and treat anything else as
unreadable, not as "no date".

Dolibarr has no "blocked" concept on contracts.
