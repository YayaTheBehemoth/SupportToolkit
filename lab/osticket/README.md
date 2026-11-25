# osTicket lab

A throwaway osTicket instance for **API contract discovery** only. Daily
development and every test run against the recorded, synthetic fixtures in
`tests/`, never against this instance. Nothing here touches work systems.

## Start and stop

Set three secrets first (in a Codespace, as Codespaces secrets; locally,
in your shell). There are no defaults, so nothing secret lives in this repo.

```sh
export OSTICKET_LAB_DB_PASSWORD=...      # any random value
export OSTICKET_LAB_INSTALL_SECRET=...   # any random value
export OSTICKET_LAB_ADMIN_PASSWORD=...   # staff login 'labadmin'
docker compose -f lab/osticket/compose.yml up -d
```

osTicket answers on `http://127.0.0.1:8080/` (staff panel: `/scp/`). Mail
goes to a host that does not exist, so nothing is sent.

`docker compose -f lab/osticket/compose.yml down -v` removes it and its data.

## An API key

In the staff panel: **Admin panel → Manage → API Keys → Add new**. The key
is bound to **one source IP address**, which is the address osTicket sees,
not yours: from the Codespace host through the published port it is the
Docker network's gateway, `docker network inspect
supporttoolkit-lab-osticket_default --format '{{(index .IPAM.Config 0).Gateway}}'`.
Allow "Can create tickets" only.

## What the ticket API does (osTicket 1.17.5, observed November 20, 2026)

`POST /api/tickets.json`, header `X-API-Key`, JSON body.

| Case | Result |
|---|---|
| Valid create | `201`, body is the **ticket number** as plain text (for example `655387`), not the internal ID. Content-Type says `text/html`. |
| Same request again, or with an `Idempotency-Key` header | `201` and **another ticket**. There is no idempotency. |
| Missing or wrong key, or right key from another IP | `401`, `Valid API key required` |
| Malformed JSON | `400`, `Syntax error, malformed JSON` |
| Missing `subject` or `message` | `500`, `Unable to create new ticket :<field id>` and a line naming the field |
| Email osTicket considers invalid (`*.invalid` included) | `500`, `Incomplete client information` |
| **Unknown `topicId`** | **`201`**: the ticket is created with no topic in the default department. Routing errors are not reported. |
| `message` as `data:text/plain;charset=utf-8,<text>` | Stored as text; commas, Unicode, `%` and a literal `data:` later in the text are kept; HTML is escaped. |

- `name` and `email` identify the requester; osTicket creates that user on first use.
- `alert: false` and `autorespond: false` suppress staff alerts and the auto-reply.
- The staff view of a ticket by number: `/scp/tickets.php?number=<number>`.
- The API cannot read tickets or list help topics, so a topic cannot be validated through it.
