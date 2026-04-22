#!/usr/bin/env bash
set -euo pipefail
#
# Triggers the ai-orchestrator /documents/index endpoint for both seeded
# tenants. Uses identity-api to mint a local JWT so the call is authenticated.
#
# Requires: jq, curl
# Assumes:  the docker-compose stack is up (make up)

IDENTITY=${IDENTITY_API_BASE_URL:-http://localhost:5004}
AI=${AI_API_BASE_URL:-http://localhost:5003}

CONTOSO_ID=11111111-1111-1111-1111-111111111111
FABRIKAM_ID=22222222-2222-2222-2222-222222222222

register_and_token () {
  local tid=$1 email=$2 pwd=$3
  # Register (idempotent — 409 Conflict is fine)
  curl -fsS -X POST "$IDENTITY/auth/register" \
    -H 'content-type: application/json' \
    -d "{\"tenantId\":\"$tid\",\"email\":\"$email\",\"password\":\"$pwd\"}" \
    >/dev/null 2>&1 || true

  # Login
  curl -fsS -X POST "$IDENTITY/auth/login" \
    -H 'content-type: application/json' \
    -d "{\"tenantId\":\"$tid\",\"email\":\"$email\",\"password\":\"$pwd\"}" \
    | jq -r '.access_token'
}

index_for () {
  local name=$1 tid=$2 email=$3
  echo "==> Indexing for $name ($tid)..."
  local token
  token=$(register_and_token "$tid" "$email" "SeedPass!42")
  curl -fsS -X POST "$AI/documents/index" \
    -H "authorization: Bearer $token" \
    -H 'content-type: application/json' \
    && echo
}

index_for "Contoso"  "$CONTOSO_ID"  "seed@contoso.test"
index_for "Fabrikam" "$FABRIKAM_ID" "seed@fabrikam.test"

echo "Indexing complete."
