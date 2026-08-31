#!/usr/bin/env bash
# Stops everything started by run-all.sh
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PIDS="${ROOT}/.run/pids"

[ -f "${PIDS}" ] || { echo "nothing to stop"; exit 0; }

while read -r pid; do
  kill "${pid}" 2>/dev/null && echo "stopped ${pid}"
done < "${PIDS}"

rm -f "${PIDS}"
