#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
PYTHON_CMD="${PYTHON_CMD:-python}"

exec "${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-performance-budget.py" \
  --dotnet "${DOTNET_CMD}" --configuration "${CONFIGURATION:-Release}" "$@"
