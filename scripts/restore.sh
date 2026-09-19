#!/usr/bin/env bash
#
# Restore a dump produced by backup.sh.
#
# DESTRUCTIVE: the target database's current contents are replaced. The script
# refuses to run without --yes so that it cannot be completed by muscle memory.
#
# Usage:
#   scripts/restore.sh --file backups/waterbilling-scheduled-20260919T101500Z.dump --yes
#
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

CONTAINER="${POSTGRES_CONTAINER:-waterbilling-postgres}"
DB_NAME="${POSTGRES_DB:-waterbilling}"
DB_USER="${POSTGRES_USER:-waterbilling}"
DUMP_FILE=""
CONFIRMED="no"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --file) DUMP_FILE="$2"; shift 2 ;;
    --container) CONTAINER="$2"; shift 2 ;;
    --database) DB_NAME="$2"; shift 2 ;;
    --user) DB_USER="$2"; shift 2 ;;
    --yes) CONFIRMED="yes"; shift ;;
    -h|--help) sed -n '2,10p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "${DUMP_FILE}" ]]; then
  # Default to the newest dump, but still require --yes.
  DUMP_FILE="$(ls -t "${REPO_ROOT}"/backups/*.dump 2>/dev/null | head -n1 || true)"
fi

if [[ -z "${DUMP_FILE}" || ! -f "${DUMP_FILE}" ]]; then
  echo "ERROR: no dump file found. Pass one with --file." >&2
  exit 1
fi

if [[ "${CONFIRMED}" != "yes" ]]; then
  echo "About to REPLACE the contents of database '${DB_NAME}' in container '${CONTAINER}'"
  echo "with: ${DUMP_FILE}"
  echo
  echo "Re-run with --yes to proceed."
  exit 1
fi

# Verify integrity before touching the target. Restoring a truncated dump over a
# working database turns one problem into two.
checksum_file="${DUMP_FILE}.sha256"
if [[ -f "${checksum_file}" ]]; then
  echo "Verifying checksum..."
  if command -v sha256sum >/dev/null 2>&1; then
    (cd "$(dirname "${DUMP_FILE}")" && sha256sum --check --status "$(basename "${checksum_file}")")
  else
    (cd "$(dirname "${DUMP_FILE}")" && shasum -a 256 --check --status "$(basename "${checksum_file}")")
  fi
  echo "Checksum OK."
else
  echo "WARNING: no checksum file alongside the dump; integrity is unverified." >&2
fi

echo "Restoring into '${DB_NAME}'..."

# --clean --if-exists drops existing objects first; --exit-on-error means a partial
# restore fails loudly instead of leaving a half-populated database that looks fine.
docker exec --interactive "${CONTAINER}" pg_restore \
  --username="${DB_USER}" \
  --dbname="${DB_NAME}" \
  --clean \
  --if-exists \
  --no-owner \
  --no-privileges \
  --exit-on-error \
  --single-transaction \
  < "${DUMP_FILE}"

echo "Restore complete."
echo "Run scripts/verify-restore.sh to confirm the data came back meaning the same thing."
