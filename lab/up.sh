#!/usr/bin/env bash
# Starts the second-provider lab (Zammad, Duplicati, Dolibarr), seeds its
# synthetic records and sets the keys SupportToolkit uses, from variables
# you own: Codespaces secrets in a codespace, or ~/.lab.env on your own
# computer (read automatically when it exists). Safe to run again:
# existing records are kept, keys are reset to the variables' values.
#
#   lab/up.sh                 start and provision
#   lab/up.sh --generate      print random values for any variable not set
#                             (lab use only): save them as Codespaces
#                             secrets, or append them to ~/.lab.env with
#                             lab/up.sh --generate >> ~/.lab.env
#   lab/up.sh --if-configured as above, but only when every variable is set;
#                             otherwise a one-line note (the dev container
#                             runs this on every codespace start)
#   lab/up.sh --links         print the addresses, logins and commands only;
#                             starts and changes nothing
#   lab/up.sh --stop          stop the lab's containers; data is kept and
#                             lab/up.sh starts them again
#   lab/up.sh --reset         delete the lab's containers and data, after
#                             confirmation; needed after changing the
#                             database passwords
#
# Data lives in lab/*/.data (git-ignored), so it survives a rebuild.
# Then: SupportToolkit configure lab   and   SupportToolkit configure use lab
set -euo pipefail

cd "$(dirname "$0")"

# A lab on your own computer keeps its variables in a file; a codespace has
# them as Codespaces secrets and no such file. LAB_ENV_FILE overrides the
# location.
lab_env="${LAB_ENV_FILE:-$HOME/.lab.env}"
if [[ -f "$lab_env" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$lab_env"
  set +a
fi

in_codespace() { [[ "${CODESPACES:-}" == "true" ]]; }

# Tools the provisioning needs. A codespace has them; a fresh Ubuntu may
# lack jq.
require_tools() {
  local tool absent=()
  for tool in "$@"; do
    command -v "$tool" > /dev/null 2>&1 || absent+=("$tool")
  done
  if (( ${#absent[@]} > 0 )); then
    echo "Missing: ${absent[*]}. On Ubuntu: sudo apt-get install -y ${absent[*]}" >&2
    echo "(Docker itself: see lab/README.md.)" >&2
    exit 1
  fi
}

# Infrastructure only: never used by SupportToolkit.
infra=(
  ZAMMAD_LAB_DB_PASSWORD ZAMMAD_LAB_ADMIN_PASSWORD
  DUPLICATI_LAB_SETTINGS_KEY
  DOLIBARR_LAB_DB_PASSWORD DOLIBARR_LAB_ADMIN_PASSWORD
)

# What SupportToolkit reads, under the names it reads them by, so the lab
# and the toolkit share one value. Enter the same values on another
# computer when `configure lab` asks.
app=(
  SUPPORTTOOLKIT_SECRET__ZAMMAD__LAB__API_TOKEN
  SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_A
  SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_B
  SUPPORTTOOLKIT_SECRET__DOLIBARR__LAB__API_KEY
)

# Addresses, logins and the variable holding each password (never a value).
links() {
  cat <<'EOF'

== Lab
  Zammad     http://127.0.0.1:8100/       login labadmin, password $ZAMMAD_LAB_ADMIN_PASSWORD
  Dolibarr   http://127.0.0.1:8090/       login labadmin, password $DOLIBARR_LAB_ADMIN_PASSWORD
  Duplicati  http://127.0.0.1:8201/ (machine-a)  password $SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_A
             http://127.0.0.1:8202/ (machine-b)  password $SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_B
  Show one:  echo $ZAMMAD_LAB_ADMIN_PASSWORD   (or: grep ZAMMAD_LAB_ADMIN_PASSWORD ~/.lab.env)

== SupportToolkit
  dotnet run --project src/SupportToolkit -- configure lab       (adds the 'lab' profile)
  dotnet run --project src/SupportToolkit -- configure use lab   (switch to it)
  Web UI: dotnet run --project src/SupportToolkit -- serve --port 5050 --no-browser
          (or the VS Code task "SupportToolkit: web UI (5050)"), then open the printed link.

EOF
  if in_codespace; then
    cat <<'EOF'
VS Code desktop forwards ports 5050, 8100, 8090, 8201 and 8202 to the same
numbers on your computer (see Ports). Keep them private. Without VS Code:
  gh codespace ports forward 5050:5050 8100:8100 8090:8090 8201:8201 8202:8202
The web UI accepts localhost/127.0.0.1 only, so a *.app.github.dev address is refused.
Installed exe on another computer: forward 8100, 8090, 8201 and 8202 the same
way, then run `SupportToolkit configure lab` there.
EOF
  else
    cat <<'EOF'
This computer reaches the lab at the addresses above. On Windows with the
lab in WSL, Windows reaches them too: lab\windows\start-lab.cmd starts the
lab, switches SupportToolkit to the lab profile and opens the web UI.
Passwords are in your variables file: grep ADMIN_PASSWORD ~/.lab.env
EOF
  fi
}

if [[ "${1:-}" == "--links" ]]; then
  links
  exit 0
fi

missing=()
for name in "${infra[@]}" "${app[@]}"; do
  [[ -n "${!name:-}" ]] || missing+=("$name")
done

if [[ "${1:-}" == "--generate" ]]; then
  require_tools openssl
  # Only the values go to standard output, so it can be appended to a file;
  # the explanation goes to the screen.
  if (( ${#missing[@]} == 0 )); then
    echo "Every lab variable is already set." >&2
  else
    {
      echo "Random values for the lab only. In a codespace, save each as a"
      echo "Codespaces secret (github.com/settings/codespaces), then rebuild or"
      echo "restart the codespace. On your own computer, append them to the"
      echo "variables file instead: lab/up.sh --generate >> ~/.lab.env"
    } >&2
    for name in "${missing[@]}"; do
      echo "$name=Lab$(openssl rand -hex 16)"
    done
  fi
  exit 0
fi

if [[ "${1:-}" == "--stop" ]]; then
  # The compose files require the variables even to stop; placeholders
  # suffice for that.
  for name in "${infra[@]}" "${app[@]}"; do
    export "$name=${!name:-unused}"
  done
  for vendor in zammad duplicati dolibarr; do
    docker compose -f "$vendor/compose.yml" stop 2>/dev/null || true
  done
  echo "Lab stopped; its data is kept. lab/up.sh starts it again."
  exit 0
fi

if [[ "${1:-}" == "--reset" ]]; then
  echo "This deletes the lab's containers and all their data (synthetic records,"
  echo "backup jobs, tickets). Your settings and secrets are not touched."
  read -r -p "Type 'reset' to continue: " answer
  [[ "$answer" == "reset" ]] || { echo "Nothing deleted."; exit 1; }
  # The compose files require the variables even to stop; placeholders
  # suffice for that, so a reset works after the secrets are lost.
  for name in "${infra[@]}" "${app[@]}"; do
    export "$name=${!name:-unused}"
  done
  for vendor in zammad duplicati dolibarr; do
    docker compose -f "$vendor/compose.yml" down --volumes --remove-orphans 2>/dev/null || true
    if [[ -d "$vendor/.data" ]]; then
      # Written by the containers' own users; removed the same way.
      docker run --rm -v "$PWD/$vendor/.data:/data" --entrypoint sh mariadb:11.4 \
        -c 'rm -rf /data/* /data/.[!.]* 2>/dev/null; true'
      rmdir "$vendor/.data" 2>/dev/null || true
    fi
  done
  echo "Lab deleted. Run lab/up.sh to start it again."
  exit 0
fi

if (( ${#missing[@]} > 0 )); then
  if [[ "${1:-}" == "--if-configured" ]]; then
    echo "Second-provider lab not started: its variables are not set (see lab/README.md)."
    exit 0
  fi
  echo "Set these first: as Codespaces secrets in a codespace, or in $lab_env" >&2
  echo "on your own computer (lab/up.sh --generate >> $lab_env creates them)." >&2
  printf '  %s\n' "${missing[@]}" >&2
  exit 1
fi

for name in "${app[@]}"; do
  if [[ "${!name}" =~ [[:space:]] ]]; then
    echo "$name must not contain spaces." >&2
    exit 1
  fi
done

wait_for() { # url, accepted status codes regex
  for _ in $(seq 1 120); do
    code=$(curl -s -o /dev/null -w '%{http_code}' "$1" || true)
    [[ "$code" =~ $2 ]] && return 0
    sleep 3
  done
  echo "Timed out waiting for $1" >&2
  exit 1
}

require_tools docker curl jq openssl

# In a codespace, Docker may still be starting when this runs.
for _ in $(seq 1 60); do
  docker info > /dev/null 2>&1 && break
  sleep 2
done

echo "== Starting containers"
for vendor in zammad duplicati dolibarr; do
  docker compose -f "$vendor/compose.yml" up -d --quiet-pull
done

# ------------------------------------------------------------------ Zammad
echo "== Zammad"
# The first start builds the database and the search index: minutes.
wait_for http://127.0.0.1:8100/ '^200$'

# No setup wizard: the admin, its group access and the API token
# SupportToolkit uses are set from your variables. Zammad gives a new token
# a random value, so the value is set afterwards. Only the runner's own
# "lab:" lines are shown.
docker compose -f zammad/compose.yml exec -T -e LAB_ADMIN_PASSWORD="$ZAMMAD_LAB_ADMIN_PASSWORD" \
  -e LAB_API_TOKEN="$SUPPORTTOOLKIT_SECRET__ZAMMAD__LAB__API_TOKEN" \
  zammad-railsserver bundle exec rails r '
UserInfo.current_user_id = 1
Setting.set("system_init_done", true)
admin = User.find_by(login: "labadmin") || User.new(login: "labadmin")
admin.assign_attributes(firstname: "Lab", lastname: "Admin", email: "labadmin@example.com",
  active: true, password: ENV.fetch("LAB_ADMIN_PASSWORD"), roles: Role.where(name: %w[Admin Agent]))
admin.save!
admin.group_names_access_map = { "Users" => "full" }
admin.save!
Token.where(user_id: admin.id, action: "api", name: "SupportToolkit lab").destroy_all
api = Token.create!(action: "api", persistent: true, user_id: admin.id, name: "SupportToolkit lab",
  preferences: { permission: %w[admin ticket.agent] })
api.update_column(:token, ENV.fetch("LAB_API_TOKEN"))
puts "lab: admin and API token set"
' < /dev/null 2>/dev/null | grep '^lab:' || { echo "Zammad setup failed" >&2; exit 1; }

zapi=http://127.0.0.1:8100/api/v1
zauth=(-H "Authorization: Token token=$SUPPORTTOOLKIT_SECRET__ZAMMAD__LAB__API_TOKEN"
  -H 'Content-Type: application/json')

if [[ "$(curl -s "$zapi/organizations?per_page=100" "${zauth[@]}" \
         | jq '[.[] | select(.name == "Northwind Traders ApS")] | length')" != "0" ]]; then
  echo "Records exist, kept."
else
  zpost() { # path, json; prints the new id
    curl -s -X POST "$zapi/$1" "${zauth[@]}" -d "$2" | jq -e -r .id \
      || { echo "Zammad refused POST $1" >&2; exit 1; }
  }
  for org in "Northwind Traders ApS" "Contoso A/S" "Fabrikam"; do
    zpost organizations "$(jq -n --arg n "$org" '{name: $n}')" > /dev/null
  done
  customer() { # first, last, email, organization
    zpost users "$(jq -n --arg f "$1" --arg l "$2" --arg e "$3" --arg o "$4" \
      '{firstname: $f, lastname: $l, email: $e, organization: $o, roles: ["Customer"]}')" > /dev/null
  }
  customer Nina  Nordvik  nina@northwind.example  "Northwind Traders ApS"
  customer Ole   Okker    ole@northwind.example   "Northwind Traders ApS"
  customer Carla Conti    carla@contoso.example   "Contoso A/S"
  customer Frank Fabri    frank@fabrikam.example  "Fabrikam"
  ticket() { # customer email, state, title
    zpost tickets "$(jq -n --arg c "$1" --arg s "$2" --arg t "$3" \
      '{title: $t, group: "Users", customer: $c, state: $s,
        article: {subject: $t, body: ("Synthetic lab ticket: " + $t), type: "note", internal: false}}')" > /dev/null
  }
  ticket nina@northwind.example  closed "Backup failed on file server"
  ticket nina@northwind.example  closed "Restore request: project folder"
  ticket ole@northwind.example   open   "Backup job warning: partly missing source"
  ticket carla@contoso.example   closed "New laptop setup"
  ticket carla@contoso.example   new    "Monitoring alert: never-run backup job"
  ticket frank@fabrikam.example  closed "Offboarding: remove shared mailbox"
  ticket frank@fabrikam.example  closed "Contract ended: archive tickets"
  echo "Organizations, customers and tickets created."
fi

# --------------------------------------------------------------- Duplicati
echo "== Duplicati"
seed_duplicati() { # port, password, then "name|target|source" job specs
  local port=$1 password=$2; shift 2
  wait_for "http://127.0.0.1:$port/" '^200$'
  local token
  token=$(curl -s -X POST "http://127.0.0.1:$port/api/v1/auth/login" \
    -H 'Content-Type: application/json' \
    -d "$(jq -n --arg p "$password" '{Password: $p, RememberMe: false}')" | jq -r .AccessToken)
  local auth=(-H "Authorization: Bearer $token")

  if [[ "$(curl -s "http://127.0.0.1:$port/api/v1/backups" "${auth[@]}" | jq length)" != "0" ]]; then
    echo "machine on $port: jobs exist, kept."
    return
  fi

  local passphrase ids=() spec name target source id
  passphrase=$(openssl rand -hex 12)
  for spec in "$@"; do
    IFS='|' read -r name target source <<< "$spec"
    id=$(curl -s -X POST "http://127.0.0.1:$port/api/v1/backups" "${auth[@]}" \
      -H 'Content-Type: application/json' \
      -d "$(jq -n --arg n "$name" --arg t "$target" --arg s "$source" --arg p "$passphrase" \
        '{Backup: {Name: $n, Description: "", Tags: [], TargetURL: $t, Sources: ($s | split(",")),
          Settings: [{Name: "passphrase", Value: $p}, {Name: "encryption-module", Value: "aes"}],
          Filters: [], Metadata: {}}, Schedule: null}')" | jq -r .ID)
    [[ "$name" == "Never run" ]] || ids+=("$id")
  done

  for id in "${ids[@]}"; do
    curl -s -o /dev/null -X POST "http://127.0.0.1:$port/api/v1/backup/$id/start" "${auth[@]}"
    sleep 2
    for _ in $(seq 1 60); do
      curl -s "http://127.0.0.1:$port/api/v1/serverstate" "${auth[@]}" \
        | jq -e '.ActiveTask == null' > /dev/null && break
      sleep 2
    done
  done
  echo "machine on $port: ${#} jobs created, ${#ids[@]} run."
}

seed_duplicati 8201 "$SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_A" \
  "Documents|file:///data/dest-documents|/source/" \
  "Broken destination|file:///proc/forbidden-dest|/source/"

seed_duplicati 8202 "$SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_B" \
  "Documents|file:///data/dest-documents|/source/" \
  "Partly missing|file:///data/dest-partly|/source/,/source-missing/" \
  "Never run|file:///data/dest-never|/source/"

# ---------------------------------------------------------------- Dolibarr
echo "== Dolibarr"
wait_for http://127.0.0.1:8090/api/index.php/status '^(200|401|403)$'
for _ in $(seq 1 60); do
  docker compose -f dolibarr/compose.yml logs dolibarr 2>&1 | grep -q "Activating module Service" && break
  sleep 3
done

# Dolibarr stores keys encrypted, so its own code sets it.
docker compose -f dolibarr/compose.yml exec -T \
  -e LAB_KEY="$SUPPORTTOOLKIT_SECRET__DOLIBARR__LAB__API_KEY" -w /var/www/scripts dolibarr php -r '
  define("NOSESSION", 1); require "../htdocs/master.inc.php";
  $sql = "UPDATE ".MAIN_DB_PREFIX."user SET api_key = \x27"
    .$db->escape(dolEncrypt(getenv("LAB_KEY"), "", "", "dolibarr"))."\x27 WHERE login = \x27labadmin\x27";
  exit($db->query($sql) ? 0 : 1);'
echo "API key set."

api=http://127.0.0.1:8090/api/index.php
dauth=(-H "DOLAPIKEY: $SUPPORTTOOLKIT_SECRET__DOLIBARR__LAB__API_KEY"
  -H 'Content-Type: application/json' -H 'Accept: application/json')
post() { curl -s -X POST "$api/$1" "${dauth[@]}" -d "$2"; }
put() { curl -s -o /dev/null -X PUT "$api/$1" "${dauth[@]}" -d "$2"; }

# An empty list is 404 "No third parties found", not [].
if [[ "$(curl -s "$api/thirdparties?limit=1" "${dauth[@]}" | jq 'if type == "array" then length else 0 end')" != "0" ]]; then
  echo "Records exist, kept."
else
  start=$(date -d 2026-01-01 +%s)
  now=$(date +%s)
  customer() { post thirdparties "$(jq -n --arg n "$1" '{name: $n, client: 1, code_client: "-1"}')"; }
  service() { post products "$(jq -n --arg r "$1" --arg l "$2" '{ref: $r, label: $l, type: 1, status: 1, status_buy: 0}')"; }
  contract() { post contracts "{\"socid\":$1,\"date_contrat\":$start,\"commercial_signature_id\":1,\"commercial_suivi_id\":1}"; }
  line() { post "contracts/$1/lines" "{\"fk_product\":$2,\"desc\":\"\",\"qty\":$3,\"subprice\":10,\"tva_tx\":0,\"date_start\":$start,\"date_end\":null}"; }

  northwind=$(customer "Northwind Traders ApS")
  contoso=$(customer "Contoso A/S")
  fabrikam=$(customer "Fabrikam")
  monitoring=$(service EXAMPLE-MONITORING "Backup monitoring")
  backup=$(service EXAMPLE-BACKUP "Backup service")

  # Northwind: monitored and backed up. Contoso: monitoring ended.
  # Fabrikam: monitoring sold but never activated.
  c1=$(contract "$northwind"); l11=$(line "$c1" "$monitoring" 1); l12=$(line "$c1" "$backup" 250)
  c2=$(contract "$contoso"); l21=$(line "$c2" "$backup" 100); l22=$(line "$c2" "$monitoring" 1)
  c3=$(contract "$fabrikam"); line "$c3" "$monitoring" 1 > /dev/null

  for c in "$c1" "$c2" "$c3"; do post "contracts/$c/validate" '{"notrigger":0}' > /dev/null; done
  for pair in "$c1 $l11" "$c1 $l12" "$c2 $l21" "$c2 $l22"; do
    set -- $pair; put "contracts/$1/lines/$2/activate" "{\"datestart\":$start}"
  done
  put "contracts/$c2/lines/$l22/unactivate" "{\"datestart\":$now}"
  echo "Customers, services and contracts created."
fi

echo
echo "Lab ready."
links
