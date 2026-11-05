#!/usr/bin/env bash
# End-to-end demo against a running stack (dotnet run --project src/DistrictSync.AppHost).
# Usage: scripts/demo.sh [API_BASE_URL]   (default: http://localhost:5080)
set -euo pipefail
API="${1:-http://localhost:5080}"
HR_CSV="$(dirname "$0")/../samples/hr_employees.csv"
PAYROLL_CSV="$(dirname "$0")/../samples/payroll_employees.csv"

need() { command -v "$1" >/dev/null || { echo "missing dependency: $1"; exit 1; }; }
need curl; need jq

echo "▶ requesting a development token for tenant 'northgate'"
TOKEN=$(curl -sf "$API/api/v1/auth/dev-token" -H 'content-type: application/json' \
  -d '{"tenantSlug":"northgate","userId":"demo-analyst","roles":["admin","analyst","approver"]}' | jq -r .accessToken)
AUTH=(-H "Authorization: Bearer $TOKEN")

echo "▶ looking up source systems"
SOURCES=$(curl -sf "$API/api/v1/source-systems" "${AUTH[@]}")
HR_ID=$(echo "$SOURCES" | jq -r '.[] | select(.code=="HR") | .id')
PAYROLL_ID=$(echo "$SOURCES" | jq -r '.[] | select(.code=="PAYROLL") | .id')
echo "   HR=$HR_ID PAYROLL=$PAYROLL_ID"

upload() {
  local source_id="$1" file="$2"
  curl -sf "$API/api/v1/ingestion/batches/csv?sourceSystemId=$source_id&entityType=Employee" "${AUTH[@]}" -F "file=@$file;type=text/csv" | jq -r .id
}
wait_batch() {
  local id="$1"
  for _ in $(seq 1 120); do
    local status
    status=$(curl -sf "$API/api/v1/ingestion/batches/$id" "${AUTH[@]}" | jq -r .status)
    [[ "$status" == "Completed" || "$status" == "Failed" || "$status" == "Duplicate" ]] && { echo "   batch $id → $status"; return; }
    sleep 0.5
  done
  echo "   batch $id did not finish in time"; exit 1
}

echo "▶ uploading HR and payroll snapshots"
HR_BATCH=$(upload "$HR_ID" "$HR_CSV"); PAYROLL_BATCH=$(upload "$PAYROLL_ID" "$PAYROLL_CSV")
wait_batch "$HR_BATCH"; wait_batch "$PAYROLL_BATCH"

echo "▶ starting reconciliation HR ↔ PAYROLL"
RUN_ID=$(curl -sf "$API/api/v1/reconciliation/runs" "${AUTH[@]}" -H 'content-type: application/json' \
  -d "{\"entityType\":\"Employee\",\"leftSourceSystemId\":\"$HR_ID\",\"rightSourceSystemId\":\"$PAYROLL_ID\"}" | jq -r .id)
for _ in $(seq 1 120); do
  RUN=$(curl -sf "$API/api/v1/reconciliation/runs/$RUN_ID" "${AUTH[@]}")
  STATUS=$(echo "$RUN" | jq -r .status)
  [[ "$STATUS" == "Completed" || "$STATUS" == "Failed" ]] && break
  sleep 0.5
done
echo "   run $RUN_ID → $STATUS"
echo "$RUN" | jq '.stats'

echo "▶ open cases"
curl -sf "$API/api/v1/cases?pageSize=20" "${AUTH[@]}" | jq -r '.items[] | "   [\(.priority)] \(.state) — \(.title)"'

echo "▶ dashboard summary"
curl -sf "$API/api/v1/dashboard/summary" "${AUTH[@]}" | jq '{openDiscrepancies, casesOpen, casesOverdue, autoResolvedToday, deadLetters, outboxPending}'
echo "✔ done — open the dashboard to watch live updates, or the Aspire dashboard for traces."
