#!/usr/bin/env bash
#
# Deploy a release to an on-premise site.
#
# Takes a backup before touching anything, recreates the containers, waits for
# readiness, and rolls back automatically if the health check fails. Both of those
# exist because a failed upgrade on a customer's site is a van dispatch, not a
# `git revert`.
#
# Usage:
#   scripts/deploy.sh --tag 1.4.0
#   scripts/deploy.sh --tag 1.4.0 --registry 123456789012.dkr.ecr.eu-west-2.amazonaws.com
#   scripts/deploy.sh --build            # build locally instead of pulling
#
set -Eeuo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

TAG=""
REGISTRY="${REGISTRY:-}"
BUILD_LOCALLY="no"
SKIP_BACKUP="no"
HEALTH_URL="${HEALTH_URL:-http://localhost:8080/health/ready}"
HEALTH_TIMEOUT_SECONDS=120

while [[ $# -gt 0 ]]; do
  case "$1" in
    --tag) TAG="$2"; shift 2 ;;
    --registry) REGISTRY="$2"; shift 2 ;;
    --build) BUILD_LOCALLY="yes"; shift ;;
    # Only for a first install, where there is nothing to back up yet.
    --skip-backup) SKIP_BACKUP="yes"; shift ;;
    --health-url) HEALTH_URL="$2"; shift 2 ;;
    -h|--help) sed -n '2,14p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

cd "${REPO_ROOT}"

log() { printf '\n[%s] %s\n' "$(date -u +%H:%M:%SZ)" "$*"; }

if [[ ! -f .env ]]; then
  echo "ERROR: .env is missing. Copy .env.example and set real secrets before deploying." >&2
  exit 1
fi

# Refuse to deploy with the example secrets still in place. This is the single most
# likely way a site ends up in production with a signing key from a public repo.
if grep -qE '^JWT__SIGNINGKEY=dev-only-signing-key' .env; then
  echo "ERROR: .env still contains the example JWT signing key." >&2
  echo "Generate one with: openssl rand -base64 48" >&2
  exit 1
fi

if grep -qE '^POSTGRES_PASSWORD=change-me-locally' .env; then
  echo "ERROR: .env still contains the example database password." >&2
  exit 1
fi

# --- 1. Record what we are rolling back TO --------------------------------
PREVIOUS_TAG="$(docker inspect --format '{{index .Config.Labels "org.opencontainers.image.version"}}' \
  waterbilling-api 2>/dev/null || echo "")"
log "Current deployed version: ${PREVIOUS_TAG:-unknown}"

# --- 2. Back up before changing anything ----------------------------------
if [[ "${SKIP_BACKUP}" == "yes" ]]; then
  log "Skipping pre-deployment backup (--skip-backup)."
else
  log "Taking a pre-deployment backup..."
  scripts/backup.sh --label "pre-deploy-${TAG:-local}"
fi

# --- 3. Fetch or build the images ------------------------------------------
if [[ "${BUILD_LOCALLY}" == "yes" ]]; then
  log "Building images locally..."
  docker compose build
else
  if [[ -z "${TAG}" ]]; then
    echo "ERROR: --tag is required unless --build is given." >&2
    exit 2
  fi
  log "Pulling ${REGISTRY:+${REGISTRY}/}waterbilling:${TAG}..."
  export WATERBILLING_TAG="${TAG}" WATERBILLING_REGISTRY="${REGISTRY}"
  docker compose pull
fi

# --- 4. Recreate ------------------------------------------------------------
log "Recreating services..."
docker compose up -d --remove-orphans

# --- 5. Verify --------------------------------------------------------------
log "Waiting for readiness at ${HEALTH_URL}..."
healthy="no"
for _ in $(seq 1 "${HEALTH_TIMEOUT_SECONDS}"); do
  if curl --fail --silent --max-time 3 "${HEALTH_URL}" >/dev/null 2>&1; then
    healthy="yes"
    break
  fi
  sleep 1
done

if [[ "${healthy}" == "yes" ]]; then
  log "Deployment successful."
  curl --silent "${HEALTH_URL}"; echo
  docker compose ps
  exit 0
fi

# --- 6. Roll back -----------------------------------------------------------
log "HEALTH CHECK FAILED after ${HEALTH_TIMEOUT_SECONDS}s. Rolling back."
docker compose logs --tail 50 api || true

if [[ -n "${PREVIOUS_TAG}" ]]; then
  log "Restoring version ${PREVIOUS_TAG}..."
  export WATERBILLING_TAG="${PREVIOUS_TAG}"
  docker compose up -d
else
  log "No previous version recorded; leaving containers as they are for inspection."
fi

cat <<'EOF'

The application was rolled back, but the DATABASE WAS NOT.

If this release applied an additive migration (new nullable column, new table) the
previous version ignores it and the rollback is complete.

If it applied a DESTRUCTIVE migration (dropped or renamed a column) the old code
expects something that no longer exists. Restore the pre-deployment backup taken in
step 2 and replay WAL to just before the migration:

    scripts/restore.sh --file backups/waterbilling-pre-deploy-<tag>-<timestamp>.dump --yes

See docs/deployment.md, "Rollback".
EOF

exit 1
