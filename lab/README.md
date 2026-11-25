# Second-provider lab

Free, self-hosted stand-ins for the systems SupportToolkit talks to, so the
provider-neutral seams can be exercised against real software without
touching work systems:

| Capability | Lab system | Port | Details |
|---|---|---|---|
| Ticketing and bulk delete | Zammad 7.3.0 | 8100 | [zammad/README.md](zammad/README.md) |
| Backup | Duplicati 2.4.1.0, two servers | 8201, 8202 | [duplicati/README.md](duplicati/README.md) |
| Commercial entitlement | Dolibarr 23.0.4 | 8090 | [dolibarr/README.md](dolibarr/README.md) |

Everything in it is synthetic. Daily development and the automated tests use
recorded fixtures, never the lab.

Zammad needs about 3 GB of memory; the rest of the lab under half a GB.
osTicket was the lab's ticketing system until November 25, 2026; it is no
longer started, because its API cannot search or delete tickets (see
[osticket/README.md](osticket/README.md)). The toolkit's osTicket provider
is unchanged.

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

- `SUPPORTTOOLKIT_SECRET__ZAMMAD__LAB__API_TOKEN`
- `SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_A`
- `SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_B`
- `SUPPORTTOOLKIT_SECRET__DOLIBARR__LAB__API_KEY`

## Use it

```sh
lab/up.sh --links   # addresses, logins, and the variable holding each password
```

VS Code desktop connected to the codespace forwards the lab's ports and the
web UI's (5050) to the same numbers on your computer, labelled in the Ports
panel. The VS Code task **Lab: addresses and logins** prints the same list.

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

## Lab on your own Windows PC (WSL)

The same lab runs on a Windows PC, in Ubuntu under WSL, with no codespace,
quota or tunnel. Windows reaches it at the same `127.0.0.1` addresses.

**Once:**

1. In an administrator terminal: `wsl --install -d Ubuntu-24.04`, restart if
   asked, and create the Ubuntu user it asks for.
2. In Ubuntu, install Docker Engine and the tools the lab uses:

   ```sh
   curl -fsSL https://get.docker.com | sudo sh   # recommends Docker Desktop and waits 20 s: let it continue
   sudo usermod -aG docker $USER
   sudo apt-get install -y jq
   ```

   Then run `wsl --shutdown` in Windows, open Ubuntu again, and check with
   `docker run --rm hello-world`. Docker Engine inside Ubuntu needs no Docker
   Desktop (whose licence is paid for larger companies).
3. Clone the repository in the Linux home (not under `/mnt/c`), create the
   lab's variables file and start the lab:

   ```sh
   git clone https://github.com/YayaTheBehemoth/SupportToolkit.git ~/SupportToolkit
   cd ~/SupportToolkit
   lab/up.sh --generate >> ~/.lab.env   # random values; keep the file private
   lab/up.sh                            # reads ~/.lab.env; ends with "Lab ready."
   ```

4. On Windows, in the repository: `dotnet run --project src\SupportToolkit -- configure lab`.
   It asks for the four keys; `grep SUPPORTTOOLKIT ~/.lab.env` in Ubuntu
   shows them (paste the part after `=`).

**Each time:** double-click or run `lab\windows\start-lab.cmd`. It starts the
lab in WSL, remembers your active profile, switches to `lab` and opens the
web UI. Press "Exit SupportToolkit" in the page when you are done, and it
switches back. `lab\windows\stop-lab.cmd` switches back on its own (after
closing the window instead), `stop-lab.cmd --down` also stops the lab, and
`stop-lab.cmd <profile>` switches to a named profile.

Both scripts assume the distribution `Ubuntu-24.04` and the clone
`~/SupportToolkit`; set `LAB_WSL_DISTRO` or `LAB_WSL_REPO` otherwise. The lab
runs the clone inside WSL, so update it there (`git pull`) to pick up lab
changes.

Notes:

- Don't click inside a running Windows terminal: its title then starts with
  "Select" and everything in it pauses until you press Esc.
- The data is kept in `~/SupportToolkit/lab/*/.data/`. After a reboot,
  `start-lab.cmd` (or `lab/up.sh`) starts the containers again.
- Coming from osTicket: add Zammad's variables to `~/.lab.env` with
  `lab/up.sh --generate >> ~/.lab.env` (it adds only those not yet set),
  stop osTicket with `docker compose -f lab/osticket/compose.yml stop`, and
  run `lab/up.sh`.

## From the installed exe on another computer

The lab runs in the codespace; the exe reaches it while the codespace is
running:

1. Forward ports 8100, 8201, 8202 and 8090 **to the same numbers** on your
   computer: VS Code desktop connected to the codespace does it on its own
   (a clash is reported, not renumbered), or `gh codespace ports forward 8100:8100 8201:8201 8202:8202 8090:8090`.
   Do not make the ports public: the lab would be on the internet.
2. Run `SupportToolkit configure lab`. On Windows it asks for the four keys
   above, hidden as you type, and stores them in the Windows Credential
   Manager; enter the same values as the Codespaces secrets.
3. `SupportToolkit configure use lab`.

The lab addresses are `http://127.0.0.1:<port>`, the same on both computers;
plain HTTP is accepted only because they are loopback addresses.
