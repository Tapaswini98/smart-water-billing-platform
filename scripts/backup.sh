#!/usr/bin/env bash
#
# Take a logical backup of the water billing database.
#
# Custom format (-Fc) rather than plain SQL: it is compressed, it can be restored
# selectively, and pg_restore can parallelise it. Plain SQL is only better when you
# need to read the dump by eye, which is not the normal case.
#
# Usage:
#   scripts/backup.sh                     # back up the running compose database
#   scripts/backup.sh --label pre-upgrade # tag the file for a specific reason
#
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly BACKUP_DIR="${BACKUP_DIR:-${REPO_ROOT}/backups}"

CONTAINER="${POSTGRES_CONTAINER:-waterbilling-postgres}"
DB_NAME="${POSTGRES_DB:-waterbilling}"
DB_USER="${POSTGRES_USER:-waterbilling}"
LABEL="scheduled"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --label) LABEL="$2"; shift 2 ;;
    --container) CONTAINER="$2"; shift 2 ;;
    --database) DB_NAME="$2"; shift 2 ;;
    --user) DB_USER="$2"; shift 2 ;;
    -h|--help) sed -n '2,12p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
outfile="${BACKUP_DIR}/waterbilling-${LABEL}-${timestamp}.dump"

mkdir -p "${BACKUP_DIR}"

if ! docker ps --format '{{.Names}}' | grep -qx "${CONTAINER}"; then
  echo "ERROR: PostgreSQL container '${CONTAINER}' is not running." >&2
  echo "Start it with: docker compose up -d postgres" >&2
  exit 1
fi

echo "Backing up '${DB_NAME}' from container '${CONTAINER}'..."

# --clean --if-exists so the dump can be restored over an existing database.
# --no-owner --no-privileges so it restores as whatever role the target uses,
# which is what makes the same dump usable on a developer machine and on site.
docker exec "${CONTAINER}" pg_dump \
  --username="${DB_USER}" \
  --dbname="${DB_NAME}" \
  --format=custom \
  --compress=9 \
  --clean \
  --if-exists \
  --no-owner \
  --no-privileges \
  > "${outfile}"

# A checksum turns "the file exists" into "the file is intact". Verified by
# verify-restore.sh before every restore.
if command -v sha256sum >/dev/null 2>&1; then
  (cd "${BACKUP_DIR}" && sha256sum "$(basename "${outfile}")" > "$(basename "${outfile}").sha256")
else
  (cd "${BACKUP_DIR}" && shasum -a 256 "$(basename "${outfile}")" > "$(basename "${outfile}").sha256")
fi

size="$(du -h "${outfile}" | cut -f1)"

echo "Backup written: ${outfile} (${size})"
echo "Checksum:       ${outfile}.sha256"
echo
echo "Next: copy this offsite. A backup that exists only on the machine it came"
echo "from does not survive the failure modes backups exist for."
