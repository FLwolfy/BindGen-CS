#!/usr/bin/env bash
set -euo pipefail

task_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python_command="${PYTHON:-python3}"
"${python_command}" "${task_root}/scripts/corpus.py" restore \
  --root "${BGCS_REAL_LIBRARY_ROOT:-${task_root}/artifacts/corpus/headers}" \
  --output "${task_root}/artifacts/acceptance/corpus-lock.json"
