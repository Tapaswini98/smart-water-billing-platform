#!/usr/bin/env bash
#
# Prove that a backup can actually be restored, and that the data still means the
# same thing afterwards.
#
# A backup that has never been restored is a hypothesis. This script turns "we take
# daily backups" into a tested claim: it starts a throwaway PostgreSQL container,
# restores the dump into it, and then asserts both structure (expected tables) and
# semantics (row counts, and an invoice total recomputed from its own line items).
#
# It touches nothing else — no existing container, no existing database.
#
# Usage:
#   scripts/verify-restore.sh                       # verify the committed sample dump
#   scripts/verify-restore.sh --file path/to.dump   # verify a specific dump
#
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

readonly VERIFY_CONTAINER="waterbilling-restore-verify-$$"
readonly VERIFY_DB="waterbilling_verify"
readonly VERIFY_USER="verify"
readonly VERIFY_PASSWORD="verify-throwaway"
readonly PG_IMAGE="${PG_IMAGE:-postgres:17-alpine}"

DUMP_FILE=""
FAILURES=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --file) DUMP_FILE="$2"; shift 2 ;;
    -h|--help) sed -n '2,15p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "${DUMP_FILE}" ]]; then
  DUMP_FILE="$(ls -t "${REPO_ROOT}"/backups/sample-*.dump 2>/dev/null | head -n1 || true)"
fi

if [[ -z "${DUMP_FILE}" || ! -f "${DUMP_FILE}" ]]; then
  echo "ERROR: no dump to verify. Expected backups/sample-*.dump or --file <path>." >&2
  exit 1
fi

cleanup() {
  docker rm --force "${VERIFY_CONTAINER}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

psql_scalar() {
  docker exec "${VERIFY_CONTAINER}" \
    psql --username="${VERIFY_USER}" --dbname="${VERIFY_DB}" \
         --tuples-only --no-align --quiet --command "$1" 2>/dev/null | tr -d '[:space:]'
}

check() {
  local description="$1" expected="$2" actual="$3"
  if [[ "${expected}" == "${actual}" ]]; then
    printf '  PASS  %-52s %s\n' "${description}" "${actual}"
  else
    printf '  FAIL  %-52s expected %s, got %s\n' "${description}" "${expected}" "${actual}"
    FAILURES=$((FAILURES + 1))
  fi
}

check_at_least() {
  local description="$1" minimum="$2" actual="$3"
  if [[ -n "${actual}" ]] && [[ "${actual}" =~ ^[0-9]+$ ]] && (( actual >= minimum )); then
    printf '  PASS  %-52s %s\n' "${description}" "${actual}"
  else
    printf '  FAIL  %-52s expected >= %s, got "%s"\n' "${description}" "${minimum}" "${actual}"
    FAILURES=$((FAILURES + 1))
  fi
}

echo "=============================================================="
echo " Backup restore verification"
echo "=============================================================="
echo " Dump:      ${DUMP_FILE}"
echo " Image:     ${PG_IMAGE}"
echo " Container: ${VERIFY_CONTAINER} (removed on exit)"
echo

# --- 1. Integrity -----------------------------------------------------------
checksum_file="${DUMP_FILE}.sha256"
if [[ -f "${checksum_file}" ]]; then
  echo "Checking dump integrity..."
  if command -v sha256sum >/dev/null 2>&1; then
    (cd "$(dirname "${DUMP_FILE}")" && sha256sum --check --status "$(basename "${checksum_file}")")
  else
    (cd "$(dirname "${DUMP_FILE}")" && shasum -a 256 --check --status "$(basename "${checksum_file}")")
  fi
  echo "  PASS  checksum matches"
else
  echo "  WARN  no checksum file alongside the dump"
fi
echo

# --- 2. Clean target --------------------------------------------------------
echo "Starting a clean PostgreSQL container..."
docker run --detach --rm \
  --name "${VERIFY_CONTAINER}" \
  --env POSTGRES_DB="${VERIFY_DB}" \
  --env POSTGRES_USER="${VERIFY_USER}" \
  --env POSTGRES_PASSWORD="${VERIFY_PASSWORD}" \
  "${PG_IMAGE}" >/dev/null

printf "Waiting for it to accept connections"
for _ in $(seq 1 60); do
  if docker exec "${VERIFY_CONTAINER}" pg_isready --username="${VERIFY_USER}" --dbname="${VERIFY_DB}" >/dev/null 2>&1; then
    break
  fi
  printf '.'
  sleep 1
done
echo " ready."
echo

# --- 3. Restore -------------------------------------------------------------
echo "Restoring..."
docker exec --interactive "${VERIFY_CONTAINER}" pg_restore \
  --username="${VERIFY_USER}" \
  --dbname="${VERIFY_DB}" \
  --no-owner \
  --no-privileges \
  --exit-on-error \
  < "${DUMP_FILE}"
echo "  PASS  pg_restore completed without error"
echo

# --- 4. Structure -----------------------------------------------------------
echo "Verifying structure..."
for table in users meters meter_api_keys meter_readings pricing_plans \
             pricing_plan_versions pricing_slabs invoices invoice_line_items \
             billing_runs billing_run_items; do
  exists="$(psql_scalar "SELECT to_regclass('public.${table}') IS NOT NULL;")"
  check "table ${table} exists" "t" "${exists}"
done

# The two indexes that carry the system's idempotency guarantees. If these do not
# survive a restore, the restored database is subtly broken in a way row counts
# would never reveal.
for index in ix_meter_readings_meter_id_reading_at_utc_unique \
             ix_invoices_meter_id_period_start_unique; do
  exists="$(psql_scalar "SELECT COUNT(*) FROM pg_indexes WHERE indexname = '${index}';")"
  check "unique index ${index}" "1" "${exists}"
done
echo

# --- 5. Data ----------------------------------------------------------------
echo "Verifying data..."
check_at_least "users restored"          1 "$(psql_scalar 'SELECT COUNT(*) FROM users;')"
check_at_least "meters restored"         1 "$(psql_scalar 'SELECT COUNT(*) FROM meters;')"
check_at_least "meter readings restored" 1 "$(psql_scalar 'SELECT COUNT(*) FROM meter_readings;')"
check_at_least "pricing plans restored"  1 "$(psql_scalar 'SELECT COUNT(*) FROM pricing_plans;')"
echo

# --- 6. Semantics -----------------------------------------------------------
# The step that makes this a verification rather than a row count: recompute each
# invoice total from its own line items and confirm it still agrees. This catches a
# restore that lost rows from a child table — which a row count on the parent would
# happily report as healthy.
echo "Recomputing invoice totals from line items..."
invoice_count="$(psql_scalar 'SELECT COUNT(*) FROM invoices;')"

if [[ "${invoice_count}" == "0" ]]; then
  echo "  SKIP  no invoices in this dump"
else
  mismatches="$(psql_scalar "
    SELECT COUNT(*) FROM (
      SELECT i.id
      FROM invoices i
      JOIN invoice_line_items l ON l.invoice_id = i.id
      GROUP BY i.id, i.total_amount
      HAVING ROUND(SUM(l.amount), 2) <> ROUND(i.total_amount, 2)
    ) AS drifted;")"
  check "invoice totals match their line items" "0" "${mismatches}"

  orphans="$(psql_scalar "
    SELECT COUNT(*) FROM invoices i
    WHERE NOT EXISTS (SELECT 1 FROM invoice_line_items l WHERE l.invoice_id = i.id);")"
  check "no invoice lost its line items" "0" "${orphans}"
fi
echo

# --- Result -----------------------------------------------------------------
echo "=============================================================="
if (( FAILURES == 0 )); then
  echo " RESULT: PASS — the backup restores and the data is intact."
  echo "=============================================================="
  exit 0
fi

echo " RESULT: FAIL — ${FAILURES} check(s) did not pass."
echo " This backup must not be relied on. Investigate before the next"
echo " scheduled run overwrites the evidence."
echo "=============================================================="
exit 1
