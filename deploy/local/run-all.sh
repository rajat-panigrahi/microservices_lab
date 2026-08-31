#!/usr/bin/env bash
# Starts every StrategyOps service locally, each on its own port with its own database.
#
# Requires RabbitMQ on localhost:5672. Either:
#   docker compose -f deploy/docker-compose.yml up -d rabbitmq
#   or a local install (sudo service rabbitmq-server start)
#
# Logs go to .run/<service>.log; stop everything with deploy/local/stop-all.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
RUN_DIR="${ROOT}/.run"
mkdir -p "${RUN_DIR}"

# service:port
SERVICES=(
  "StrategyOps.Projects.Api:5101"
  "StrategyOps.Kpi.Api:5102"
  "StrategyOps.Risk.Api:5103"
  "StrategyOps.Issues.Api:5104"
  "StrategyOps.Benefits.Api:5105"
  "StrategyOps.Reporting.Api:5106"
  "StrategyOps.Identity.Api:5107"
  "StrategyOps.Discovery.Api:5108"
)

start() {
  local name="$1" port="$2" dir="$3"
  [ -d "${dir}" ] || { echo "skip   ${name} (not built yet)"; return; }

  ASPNETCORE_URLS="http://localhost:${port}" \
  ASPNETCORE_ENVIRONMENT="Development" \
    dotnet run --project "${dir}" --no-launch-profile \
    > "${RUN_DIR}/${name}.log" 2>&1 &

  echo "$!" >> "${RUN_DIR}/pids"
  echo "start  ${name} on http://localhost:${port} (swagger at /swagger)"
}

: > "${RUN_DIR}/pids"

for entry in "${SERVICES[@]}"; do
  start "${entry%%:*}" "${entry##*:}" "${ROOT}/src/Services/${entry%%:*}"
done

start "StrategyOps.Gateway" 5100 "${ROOT}/src/Gateway/StrategyOps.Gateway"

echo
echo "Waiting for health checks..."
for entry in "${SERVICES[@]}" "StrategyOps.Gateway:5100"; do
  port="${entry##*:}"
  for _ in $(seq 1 40); do
    if curl -fsS "http://localhost:${port}/health" >/dev/null 2>&1; then
      echo "  ready  ${entry%%:*}"
      break
    fi
    sleep 1
  done
done
