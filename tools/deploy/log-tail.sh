#!/usr/bin/env bash
# log-tail.sh — SSH wrapper tail log VPS MEPAuto, parse JSON qua jq.
#
# Usage:
#   tools/deploy/log-tail.sh                # Follow live container logs
#   tools/deploy/log-tail.sh --file         # Tail rolling JSON log file (CompactJsonFormatter)
#   tools/deploy/log-tail.sh --errors       # Chỉ Error/Fatal level
#
# Yêu cầu: jq cài trên LOCAL machine (`apt install jq` / `brew install jq` / `winget install jq`).
# VPS không cần jq — pipe stream về local.

set -euo pipefail

VPS_HOST="${MEPAUTO_VPS_HOST:-root@129.212.230.159}"
DATA_DIR="${MEPAUTO_DATA_DIR:-/var/mepauto-data}"
CONTAINER="${MEPAUTO_CONTAINER:-mepauto-api}"

mode="container"
filter=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --file) mode="file"; shift ;;
    --errors) filter='select(."@l" == "Error" or ."@l" == "Fatal")'; shift ;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \?//'
      exit 0
      ;;
    *) echo "Unknown arg: $1" >&2; exit 1 ;;
  esac
done

if ! command -v jq >/dev/null 2>&1; then
  echo "WARN: jq chưa cài local — output sẽ raw JSON. Install: apt/brew/winget install jq" >&2
  jq_cmd="cat"
else
  if [[ -n "$filter" ]]; then
    jq_cmd="jq -c '$filter'"
  else
    jq_cmd="jq -r '\"\\(.[\"@t\"]) [\\(.[\"@l\"] // \"Info\")] \\(.[\"RequestId\"] // \"-\") \\(.[\"UserId\"] // \"-\") \\(.[\"@m\"])\"'"
  fi
fi

case "$mode" in
  container)
    echo "==> docker logs -f $CONTAINER (Console plain text)" >&2
    ssh "$VPS_HOST" "docker logs -f --tail 100 $CONTAINER"
    ;;
  file)
    echo "==> tail JSON log $DATA_DIR/logs/server-*.log (CompactJsonFormatter)" >&2
    # shellcheck disable=SC2029
    ssh "$VPS_HOST" "tail -F \$(ls -t $DATA_DIR/logs/server-*.log | head -1)" | eval "$jq_cmd"
    ;;
esac
