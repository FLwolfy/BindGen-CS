#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
OUTPUT_DIR="${ROOT_DIR}/artifacts/supply-chain"
REPORT_PATH="${OUTPUT_DIR}/nuget-vulnerabilities.json"
INVENTORY_PATH="${OUTPUT_DIR}/dependency-licenses.json"
mkdir -p "${OUTPUT_DIR}"

"${DOTNET_CMD}" list "${ROOT_DIR}/BindGen-CS.sln" package \
  --include-transitive --vulnerable --format json --output-version 1 > "${REPORT_PATH}"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/scripts/BGCS.DependencyAudit/BGCS.DependencyAudit.csproj" \
  --configuration Release -- vulnerabilities "${REPORT_PATH}"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/scripts/BGCS.DependencyAudit/BGCS.DependencyAudit.csproj" \
  --configuration Release -- licenses "${ROOT_DIR}" "${INVENTORY_PATH}"
