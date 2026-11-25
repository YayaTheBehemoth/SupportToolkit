# Zammad lab

The lab's ticketing system, and the second provider for the bulk delete.
It replaced osTicket in the lab on November 25, 2026, because osTicket's API
can only create tickets: it cannot search or delete them. Contract discovery
only: daily development and every test run against recorded, synthetic
fixtures in `tests/`, never against this instance.

`lab/up.sh` starts and provisions it with the rest of the lab. On its own:

```sh
export ZAMMAD_LAB_DB_PASSWORD=...      # any random value
docker compose -f lab/zammad/compose.yml up -d
```

It answers on `http://127.0.0.1:8100/` (login `labadmin`, password
`$ZAMMAD_LAB_ADMIN_PASSWORD`, set by `up.sh`). The first start builds the
database and the search index and takes a few minutes.

**Memory:** about 3 GB, 1–1.6 GB of it Elasticsearch, whose heap is capped at
1 GB in `compose.yml` (uncapped it takes half of the machine's memory).

## What `up.sh` provisions

- No setup wizard: `labadmin` (roles Admin and Agent, full access to the
  group `Users`) and the API token SupportToolkit uses, from
  `SUPPORTTOOLKIT_SECRET__ZAMMAD__LAB__API_TOKEN`. Zammad gives a new token a
  random value, so the value is set afterwards; it is stored as it is.
  The token has the permissions `admin` and `ticket.agent`.
- The lab's companies, as in Dolibarr: organizations Northwind Traders ApS,
  Contoso A/S and Fabrikam, four customers at `*.example` addresses and
  seven tickets in mixed states (new, open, closed). Zammad's own welcome
  ticket (organization Zammad Foundation) stays.

## What the API does (Zammad 7.3.0, observed November 25, 2026)

Header `Authorization: Token token=<value>`.

| Call | Result |
|---|---|
| `GET /api/v1/users/me` | The token's user (id, login, email, names, role ids). A wrong token: `401`. |
| `GET /api/v1/tickets/search?query=…` | A plain list of tickets, **no total**. |
| … with `full=true` | `record_ids`, `total_count` and `assets` (`Ticket`, `User`, `Organization`): the tickets, their customers and organizations in one answer. Ticket **state names** are not included (`state_id` only; names from `/api/v1/ticket_states`). |
| Paging | `page` and `per_page` (`total_count` stays the full count). `offset` is ignored. |
| Query: organization | `organization.name:"Contoso A/S"` (quotes keep spaces and `/`) |
| Query: requester | `customer.email:"nina@northwind.example"` |
| Query: email domain | `customer.email:*northwind.example`; `customer.email:*@northwind.example` finds **nothing** |
| Query: words | `offboarding` |
| **Empty query** | **Every ticket.** A caller must never send one. |
| `DELETE /api/v1/tickets/{id}` | `200`, `{}`; the ticket is gone at once, including from search. **There is no trash and no undo.** |
| … again, or an unknown id | `404`, `Couldn't find Ticket with 'id'=…` |
| … with a token that has `ticket.agent` but not `admin` | `403`, `Not authorized (admin permission required)!` |

So deleting tickets through the API needs Zammad's full `admin` permission,
not agent rights: whoever runs a bulk delete against Zammad must be an
administrator there.
