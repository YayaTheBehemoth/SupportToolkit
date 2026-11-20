# Second-provider lab

Free, self-hosted stand-ins for the systems SupportToolkit talks to, so the
provider-neutral seams can be exercised against real software without
touching work systems:

| Capability | Lab system | Port | Details |
|---|---|---|---|
| Ticketing | osTicket 1.17.5 | 8080 | [osticket/README.md](osticket/README.md) |
| Backup | Duplicati 2.4.1.0, two servers | 8201, 8202 | [duplicati/README.md](duplicati/README.md) |
| Commercial entitlement | Dolibarr 23.0.4 | 8090 | [dolibarr/README.md](dolibarr/README.md) |

Everything in it is synthetic. Daily development and the automated tests use
recorded fixtures, never the lab.

## Start it (in the codespace)

Every password and key comes from a variable you own; none has a default.
Save them once as Codespaces secrets (github.com/settings/codespaces):

```sh
lab/up.sh --generate   # prints random values for any variable not yet set
```

Save each printed line as a Codespaces secret, then rebuild or restart the
codespace. After that:

```sh
lab/up.sh
```

starts the three systems, seeds their synthetic records (customers,
contracts, backup jobs) and sets the keys SupportToolkit uses. It is safe to
run again: existing records are kept and the keys are reset to your values.

### It persists

- **Secrets** are Codespaces secrets, so they outlive every codespace.
- **Containers** restart on their own when the codespace restarts
  (`restart: unless-stopped`), and the dev container runs
  `lab/up.sh --if-configured` on every start, which also recreates them
  after a rebuild. Without the variables it prints one line and does nothing.
- **Data** lives in `lab/*/.data/` in the workspace (git-ignored), which a
  rebuild keeps: records, backup jobs and tickets are still there.

To start over, or after changing a database password (the stored data is
tied to it):

```sh
lab/up.sh --reset   # asks first; deletes the lab's containers and data only
lab/up.sh
```

The four keys SupportToolkit itself needs use the names SupportToolkit reads,
so in the codespace nothing more is needed:

- `SUPPORTTOOLKIT_SECRET__OSTICKET__LAB__API_KEY`
- `SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_A`
- `SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_B`
- `SUPPORTTOOLKIT_SECRET__DOLIBARR__LAB__API_KEY`

## Use it

```sh
SupportToolkit configure lab        # adds (or resets) the 'lab' profile
SupportToolkit configure use lab    # switch to it
SupportToolkit configure use work   # and back to your own profile
```

`configure lab` adds one profile named `lab` and connections named `lab` for
the three systems. Your other profiles and connections are not touched, and
the active profile does not change (unless `lab` is the first profile).
Status, the profile page and the Backup Aggregator page always name the
systems the active profile uses.

## From the installed exe on another computer

The lab runs in the codespace; the exe reaches it while the codespace is
running:

1. Forward ports 8080, 8201, 8202 and 8090 **to the same numbers** on your
   computer: VS Code desktop connected to the codespace does it in its Ports
   panel, or `gh codespace ports forward 8080:8080 8201:8201 8202:8202 8090:8090`.
   Do not make the ports public: the lab would be on the internet.
2. Run `SupportToolkit configure lab`. On Windows it asks for the four keys
   above, hidden as you type, and stores them in the Windows Credential
   Manager; enter the same values as the Codespaces secrets.
3. `SupportToolkit configure use lab`.

The lab addresses are `http://127.0.0.1:<port>`, the same on both computers;
plain HTTP is accepted only because they are loopback addresses.
