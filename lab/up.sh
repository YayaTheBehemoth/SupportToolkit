#!/usr/bin/env bash
# Starts the second-provider lab (osTicket, Duplicati, Dolibarr), seeds its
# synthetic records and sets the keys SupportToolkit uses, from variables
# you own (Codespaces secrets). Safe to run again: existing records are
# kept, keys are reset to the variables' values.
#
#   lab/up.sh              start and provision
#   lab/up.sh --generate   print random values for any variable not set,
#                          to save as Codespaces secrets (lab use only)
#
# Then: SupportToolkit configure lab   and   SupportToolkit configure use lab
set -euo pipefail

cd "$(dirname "$0")"

# Infrastructure only: never used by SupportToolkit.
infra=(
  OSTICKET_LAB_DB_PASSWORD OSTICKET_LAB_INSTALL_SECRET OSTICKET_LAB_ADMIN_PASSWORD
  DUPLICATI_LAB_SETTINGS_KEY
  DOLIBARR_LAB_DB_PASSWORD DOLIBARR_LAB_ADMIN_PASSWORD
)

# What SupportToolkit reads, under the names it reads them by, so the lab
# and the toolkit share one value. Enter the same values on another
# computer when `configure lab` asks.
app=(
  SUPPORTTOOLKIT_SECRET__OSTICKET__LAB__API_KEY
  SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_A
  SUPPORTTOOLKIT_SECRET__DUPLICATI__LAB__PASSWORD_MACHINE_B
  SUPPORTTOOLKIT_SECRET__DOLIBARR__LAB__API_KEY
)

missing=()
for name in "${infra[@]}" "${app[@]}"; do
  [[ -n "${!name:-}" ]] || missing+=("$name")
done

if [[ "${1:-}" == "--generate" ]]; then
  if (( ${#missing[@]} == 0 )); then
    echo "Every lab variable is already set."
  else
    echo "Random values for the lab only. Save each as a Codespaces secret"
    echo "(github.com/settings/codespaces), then rebuild or restart the codespace."
    echo
    for name in "${missing[@]}"; do
      echo "$name=Lab$(openssl rand -hex 16)"
    done
  fi
  exit 0
fi

if (( ${#missing[@]} > 0 )); then
  echo "Set these first (as Codespaces secrets), or run: lab/up.sh --generate" >&2
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

echo "== Starting containers"
for vendor in osticket duplicati dolibarr; do
  docker compose -f "$vendor/compose.yml" up -d --quiet-pull
done

# ---------------------------------------------------------------- osTicket
echo "== osTicket"
wait_for http://127.0.0.1:8080/ '^200$'
gateway=$(docker network inspect supporttoolkit-lab-osticket_default \
  --format '{{(index .IPAM.Config 0).Gateway}}')
# The key is bound to the address osTicket sees: the network's gateway, for
# requests through the published port (also through a forwarded port).
docker compose -f osticket/compose.yml exec -T \
  -e MYSQL_PWD="$OSTICKET_LAB_DB_PASSWORD" db mariadb -uosticket osticket -e "
  DELETE FROM ost_api_key WHERE notes = 'SupportToolkit lab';
  INSERT INTO ost_api_key (isactive, ipaddr, apikey, can_create_tickets, can_exec_cron, notes, updated, created)
  VALUES (1, '$gateway', '$SUPPORTTOOLKIT_SECRET__OSTICKET__LAB__API_KEY', 1, 0, 'SupportToolkit lab', NOW(), NOW());"
echo "API key set, bound to $gateway; help topic 10 (Report a Problem) exists by default."

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

if [[ "$(curl -s "$api/thirdparties?limit=1" "${dauth[@]}" | jq 'length')" != "0" ]]; then
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

cat <<'EOF'

== Lab ready
  osTicket   http://127.0.0.1:8080/   (staff panel /scp/, login labadmin)
  Duplicati  http://127.0.0.1:8201/ (machine-a)   http://127.0.0.1:8202/ (machine-b)
  Dolibarr   http://127.0.0.1:8090/   (login labadmin)

Next:  dotnet run --project src/SupportToolkit -- configure lab
       dotnet run --project src/SupportToolkit -- configure use lab
From another computer: forward ports 8080, 8201, 8202 and 8090 to the same
numbers, then run `SupportToolkit configure lab` there.
EOF
