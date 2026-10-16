#!/usr/bin/env bash
set -euo pipefail

task_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${task_root}/scripts/lib/common.sh"
python_command="${PYTHON:-python3}"
"${python_command}" "${task_root}/scripts/test-corpus.py" --dotnet "$(resolve_dotnet_host)" \
  --configuration "${CONFIGURATION:-Release}" --group cpp \
  --output "${task_root}/artifacts/acceptance/corpus-cpp.json"
