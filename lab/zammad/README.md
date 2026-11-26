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
  group `Users`) and two tokens, kept apart by purpose as in production.
  Zammad gives a new token a random value, so each value is set afterwards
  from your variables; it is stored as it is.
  - The **API token** (`SUPPORTTOOLKIT_SECRET__ZAMMAD__LAB__API_TOKEN`) creates
    the review's tickets. Permission `ticket.agent` only: it cannot delete.
  - The **deletion token** (`SUPPORTTOOLKIT_SECRET__ZAMMAD__LAB__DELETION_TOKEN`)
    is bulk delete's. Permissions `admin` and `ticket.agent`, since Zammad
    deletes only for `admin`. SupportToolkit refuses to delete when both
    tokens are the same.
- The lab's companies, as in Dolibarr: organizations Northwind Traders ApS,
  Contoso A/S and Fabrikam, four customers at `*.example` addresses and
  seven tickets in mixed states (new, open, closed). Zammad's own welcome
  ticket (organization Zammad Foundation) stays.
- For bulk delete, **Tailspin Toys**, a customer that has left: three
  requesters on two domains (`tailspin.example`, `tailspin-partner.example`)
  and 110 tickets, more than one search page: 104 closed, 4 open, 2 new.
  `lab/up.sh --more-tickets` adds another 110 after a bulk delete.

An existing `~/.lab.env` from before the deletion token lacks it:
`lab/up.sh --generate >> ~/.lab.env` adds only what is missing. Then run
`configure lab` on Windows again; it asks for the new token.

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

Observed November 26, 2026:

| Question | Answer |
|---|---|
| Ticket ID and number | Different: ID `13` is `Ticket#44013` in Zammad. The API deletes by ID; operators know the number. |
| Ticket states | `GET /api/v1/ticket_states`: `new` 1, `open` 2, `pending reminder` 3, `closed` 4, `merged` 5, `pending close` 6. |
| Ticket text in a search | Not included (`article_ids` only); one more call per ticket. |
| Sender name | `customer.fullname:"Nina Nordvik"` (also `customer.firstname`, `customer.lastname`). |
| Combining | `organization.name:"Contoso A/S" AND state.name:closed` |
| Loose matches | A quoted organization is a phrase within the name, and a domain is an email suffix (`*contoso.example` also matches `notcontoso.example`). SupportToolkit keeps only exact matches. |
| A `ticket.agent`-only token | Creates a ticket with `customer_id: "guess:…"` (201). |
| Role names | `GET /api/v1/roles`; a user carries `role_ids` only. |
