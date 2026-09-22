#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
CONFIGURATION="${CONFIGURATION:-Release}"
DECLARATION_COUNT="${BGCS_PERF_DECLARATIONS:-10000}"
COLD_BUDGET_SECONDS="${BGCS_PERF_COLD_BUDGET_SECONDS:-60}"
WARM_BUDGET_SECONDS="${BGCS_PERF_WARM_BUDGET_SECONDS:-10}"
PERF_DIR="${ROOT_DIR}/artifacts/performance"

rm -rf "${PERF_DIR}"
mkdir -p "${PERF_DIR}"

awk -v count="${DECLARATION_COUNT}" 'BEGIN { for (i = 0; i < count; i++) printf "int bgcs_perf_%d(int value);\n", i }' > "${PERF_DIR}/large.h"
cat > "${PERF_DIR}/bindgen.json" <<EOF
{
  "Preset": "host-c,c-library",
  "Namespace": "BGCS.Performance.Generated",
  "ApiName": "PerformanceApi",
  "LibName": "performance",
  "EntryFiles": ["large.h"],
  "OutputPath": "Generated",
  "EnableIncrementalCache": true,
  "CacheDirectory": ".cache"
}
EOF

run_generation() {
  "${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" \
    --configuration "${CONFIGURATION}" --no-build -- generate "${PERF_DIR}/bindgen.json"
}

hash_file() {
  if command -v sha256sum > /dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

start_seconds="$(date +%s)"
run_generation
cold_seconds="$(( $(date +%s) - start_seconds ))"

bindings="${PERF_DIR}/Generated/Bindings.cs"
if [[ ! -f "${bindings}" ]]; then
  printf '[performance] Missing generated output after cold run: %s\n' "${bindings}" >&2
  exit 1
fi
cold_hash="$(hash_file "${bindings}")"
rm -rf "${PERF_DIR}/Generated"

start_seconds="$(date +%s)"
run_generation
warm_seconds="$(( $(date +%s) - start_seconds ))"

if [[ ! -f "${bindings}" ]]; then
  printf '[performance] Cache restoration did not recreate output: %s\n' "${bindings}" >&2
  exit 1
fi
warm_hash="$(hash_file "${bindings}")"
if [[ "${cold_hash}" != "${warm_hash}" ]]; then
  printf '[performance] Warm cache output hash differs from cold generation.\n' >&2
  exit 1
fi
cache_entries="$(find "${PERF_DIR}/.cache" -name entry.json -type f | wc -l | tr -d ' ')"
if [[ "${cache_entries}" != "1" ]]; then
  printf '[performance] Expected exactly one immutable cache entry, found %s.\n' "${cache_entries}" >&2
  exit 1
fi
emitted_count="$(grep -c 'Native(' "${bindings}" || true)"
if (( emitted_count < DECLARATION_COUNT )); then
  printf '[performance] Expected at least %s native methods, found %s.\n' "${DECLARATION_COUNT}" "${emitted_count}" >&2
  exit 1
fi
if (( cold_seconds > COLD_BUDGET_SECONDS )); then
  printf '[performance] Cold generation exceeded budget: %ss > %ss.\n' "${cold_seconds}" "${COLD_BUDGET_SECONDS}" >&2
  exit 1
fi
if (( warm_seconds > WARM_BUDGET_SECONDS )); then
  printf '[performance] Warm cache restoration exceeded budget: %ss > %ss.\n' "${warm_seconds}" "${WARM_BUDGET_SECONDS}" >&2
  exit 1
fi

cat > "${PERF_DIR}/report.json" <<EOF
{
  "status": "passed",
  "declarations": ${DECLARATION_COUNT},
  "emittedNativeMethods": ${emitted_count},
  "coldSeconds": ${cold_seconds},
  "coldBudgetSeconds": ${COLD_BUDGET_SECONDS},
  "warmSeconds": ${warm_seconds},
  "warmBudgetSeconds": ${WARM_BUDGET_SECONDS},
  "deterministicOutputSha256": "${warm_hash}",
  "cacheEntries": ${cache_entries},
  "restoredAfterOutputDeletion": true
}
EOF
printf '[performance] %s declarations passed: cold=%ss/%ss, warm=%ss/%ss.\n' \
  "${DECLARATION_COUNT}" "${cold_seconds}" "${COLD_BUDGET_SECONDS}" "${warm_seconds}" "${WARM_BUDGET_SECONDS}"
