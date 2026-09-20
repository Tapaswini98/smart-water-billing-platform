#!/usr/bin/env bash
#
# Take a logical backup of the water billing database.
#
# Custom format (-Fc) rather than plain SQL: compressed, selectively restorable, and
# parallelisable by pg_restore. It is also unmistakably a backup rather than a
# migration script, which matters when the artifact is reviewed by someone else.
#
# Works two ways, because an on-premise box may run PostgreSQL in a container or
# directly on the host:
#   scripts/backup.sh                          # auto-detect: container if running, else local
#   scripts/backup.sh --local                  # force local pg_dump over TCP
#   scripts/backup.sh --container waterbilling-postgres
#   scripts/backup.sh --label pre-upgrade      # tag the file with a reason
#
# Writes <name>.dump, <name>.dump.sha256 and <name>.manifest.md. The manifest is
# what turns a committed file into evidence: tool versions, row counts and the exact
# command used, so a restore can be checked against what was actually captured.
#
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly BACKUP_DIR="${BACKUP_DIR:-${REPO_ROOT}/backups}"

CONTAINER="${POSTGRES_CONTAINER:-waterbilling-postgres}"
DB_NAME="${POSTGRES_DB:-waterbilling}"
DB_USER="${POSTGRES_USER:-waterbilling}"
DB_HOST="${PGHOST:-localhost}"
DB_PORT="${PGPORT:-5432}"
LABEL="scheduled"
MODE="auto"
OUTPUT_NAME=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --label) LABEL="$2"; shift 2 ;;
    --container) CONTAINER="$2"; MODE="container"; shift 2 ;;
    --local) MODE="local"; shift ;;
    --database) DB_NAME="$2"; shift 2 ;;
    --user) DB_USER="$2"; shift 2 ;;
    --host) DB_HOST="$2"; shift 2 ;;
    --port) DB_PORT="$2"; shift 2 ;;
    --name) OUTPUT_NAME="$2"; shift 2 ;;
    -h|--help) sed -n '2,22p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

if [[ "${MODE}" == "auto" ]]; then
  if docker ps --format '{{.Names}}' 2>/dev/null | grep -qx "${CONTAINER}"; then
    MODE="container"
  else
    MODE="local"
  fi
fi

mkdir -p "${BACKUP_DIR}"

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
basename_out="${OUTPUT_NAME:-waterbilling-${LABEL}-${timestamp}}"
outfile="${BACKUP_DIR}/${basename_out}.dump"
manifest="${BACKUP_DIR}/${basename_out}.manifest.md"

# --clean --if-exists so the dump can be restored over an existing database.
# --no-owner --no-privileges so it restores as whatever role the target uses, which
# is what makes one dump usable on a developer machine and on site.
DUMP_ARGS=(--format=custom --compress=9 --clean --if-exists --no-owner --no-privileges)

run_psql() {
  if [[ "${MODE}" == "container" ]]; then
    docker exec "${CONTAINER}" psql --username="${DB_USER}" --dbname="${DB_NAME}" --tuples-only --no-align --quiet --command "$1"
  else
    psql --host="${DB_HOST}" --port="${DB_PORT}" --username="${DB_USER}" --dbname="${DB_NAME}" --tuples-only --no-align --quiet --command "$1"
  fi
}

echo "Backing up '${DB_NAME}' (${MODE} mode)..."

if [[ "${MODE}" == "container" ]]; then
  if ! docker ps --format '{{.Names}}' | grep -qx "${CONTAINER}"; then
    echo "ERROR: PostgreSQL container '${CONTAINER}' is not running." >&2
    echo "Start it with: docker compose up -d postgres" >&2
    exit 1
  fi
  pg_dump_version="$(docker exec "${CONTAINER}" pg_dump --version)"
  server_version="$(run_psql 'SHOW server_version;' | tr -d '[:space:]')"
  docker exec "${CONTAINER}" pg_dump --username="${DB_USER}" --dbname="${DB_NAME}" "${DUMP_ARGS[@]}" > "${outfile}"
  dump_command="docker exec ${CONTAINER} pg_dump --username=${DB_USER} --dbname=${DB_NAME} ${DUMP_ARGS[*]}"
else
  pg_dump_version="$(pg_dump --version)"
  server_version="$(run_psql 'SHOW server_version;' | tr -d '[:space:]')"
  pg_dump --host="${DB_HOST}" --port="${DB_PORT}" --username="${DB_USER}" --dbname="${DB_NAME}" "${DUMP_ARGS[@]}" > "${outfile}"
  dump_command="pg_dump --host=${DB_HOST} --port=${DB_PORT} --username=${DB_USER} --dbname=${DB_NAME} ${DUMP_ARGS[*]}"
fi

sha_tool() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$@"; else shasum -a 256 "$@"; fi; }
(cd "${BACKUP_DIR}" && sha_tool "$(basename "${outfile}")" > "$(basename "${outfile}").sha256")

checksum="$(cut -d' ' -f1 < "${outfile}.sha256")"
size_bytes="$(wc -c < "${outfile}" | tr -d ' ')"
size_human="$(du -h "${outfile}" | cut -f1 | tr -d ' ')"

# Row counts per table. A restore is verified against these, which is what catches a
# dump that completed but lost a child table.
tables="users meters meter_api_keys meter_readings meter_reset_events pricing_plans pricing_plan_versions pricing_slabs invoices invoice_line_items billing_runs billing_run_items payments payment_events audit_log"

{
  echo "# Backup manifest"
  echo
  echo "| Field | Value |"
  echo "|---|---|"
  echo "| File | \`$(basename "${outfile}")\` |"
  echo "| Label | ${LABEL} |"
  echo "| Taken at (UTC) | $(date -u +%Y-%m-%dT%H:%M:%SZ) |"
  echo "| Database | ${DB_NAME} |"
  echo "| PostgreSQL server | ${server_version} |"
  echo "| Tool | ${pg_dump_version} |"
  echo "| Format | custom (\`-Fc\`), compression 9 |"
  echo "| Size | ${size_human} (${size_bytes} bytes) |"
  echo "| SHA-256 | \`${checksum}\` |"
  echo
  echo "## Command used"
  echo
  echo '```bash'
  echo "${dump_command}"
  echo '```'
  echo
  echo "## Row counts at capture"
  echo
  echo "| Table | Rows |"
  echo "|---|---:|"
  for table in ${tables}; do
    count="$(run_psql "SELECT COUNT(*) FROM ${table};" 2>/dev/null | tr -d '[:space:]' || echo '-')"
    echo "| \`${table}\` | ${count:-0} |"
  done
  echo
  echo "## Restoring"
  echo
  echo 'Custom-format dumps need `pg_restore`, not `psql`:'
  echo
  echo '```bash'
  echo "scripts/verify-restore.sh --file backups/$(basename "${outfile}")   # safe: throwaway container"
  echo "scripts/restore.sh --file backups/$(basename "${outfile}") --yes    # destructive: replaces the target"
  echo '```'
  echo
  echo 'The dump includes `__EFMigrationsHistory`, so EF Core sees no pending'
  echo 'migrations after a restore and startup migration is a no-op.'
} > "${manifest}"

echo "Backup:   ${outfile} (${size_human})"
echo "Checksum: ${outfile}.sha256"
echo "Manifest: ${manifest}"
echo
echo "Next: copy this offsite. A backup that exists only on the machine it came from"
echo "does not survive the failure modes backups exist for."
