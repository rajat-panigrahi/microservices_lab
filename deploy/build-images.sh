#!/usr/bin/env bash
# Builds every service image from the one shared Dockerfile.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TAG="${1:-local}"

build() {
  local path="$1" assembly="$2" image="$3"
  echo "==> ${image}:${TAG}"
  docker build \
    -f "${ROOT}/deploy/docker/Dockerfile" \
    --build-arg "PROJECT=${path}" \
    --build-arg "ASSEMBLY=${assembly}" \
    -t "strategyops/${image}:${TAG}" \
    "${ROOT}"
}

build src/Gateway/StrategyOps.Gateway            StrategyOps.Gateway          gateway
build src/Services/StrategyOps.Projects.Api      StrategyOps.Projects.Api     projects
build src/Services/StrategyOps.Kpi.Api           StrategyOps.Kpi.Api          kpi
build src/Services/StrategyOps.Risk.Api          StrategyOps.Risk.Api         risk
build src/Services/StrategyOps.Issues.Api        StrategyOps.Issues.Api       issues
build src/Services/StrategyOps.Benefits.Api      StrategyOps.Benefits.Api     benefits
build src/Services/StrategyOps.Reporting.Api     StrategyOps.Reporting.Api    reporting
build src/Services/StrategyOps.Identity.Api      StrategyOps.Identity.Api     identity
build src/Services/StrategyOps.Discovery.Api     StrategyOps.Discovery.Api    discovery

echo
echo "Done. Now: docker compose -f deploy/docker-compose.yml up"
