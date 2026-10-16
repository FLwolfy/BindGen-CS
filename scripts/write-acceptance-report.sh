#!/usr/bin/env bash
set -euo pipefail

task_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python_command="${PYTHON:-python3}"
if [[ -z "${BGCS_ACCEPTANCE_GATE_DIR:-}" || -z "${BGCS_ACCEPTANCE_TEST_ROOT:-}" ]]; then
  printf 'Run the full matrix first; current invocation gate and TRX roots are required.\n' >&2
  exit 1
fi
exec "${python_command}" "${task_root}/scripts/write-acceptance-report.py" \
  --gate-root "${BGCS_ACCEPTANCE_GATE_DIR}" --test-root "${BGCS_ACCEPTANCE_TEST_ROOT}" \
  --output "${task_root}/artifacts/acceptance"
