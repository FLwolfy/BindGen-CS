#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
CONFIGURATION="${CONFIGURATION:-Release}"
BASELINE_DIR="${ROOT_DIR}/tests/public-api"
CURRENT_DIR="${ROOT_DIR}/artifacts/public-api"
mkdir -p "${CURRENT_DIR}"

projects=(BGCS.Intermediate BGCS.CppAst BGCS.Core BGCS.Language BGCS BGCS.Cpp2C BGCS.Runtime)
# BGCS.ApiSnapshot is not part of BindGen-CS.sln, so the release job's solution
# restore does not create its assets file on a fresh runner. Build restores it here.
"${DOTNET_CMD}" build "${ROOT_DIR}/scripts/BGCS.ApiSnapshot/BGCS.ApiSnapshot.csproj" \
  --configuration "${CONFIGURATION}"

for project in "${projects[@]}"; do
  assembly="${ROOT_DIR}/src/${project}/bin/${CONFIGURATION}/net9.0/${project}.dll"
  baseline="${BASELINE_DIR}/${project}.txt"
  current="${CURRENT_DIR}/${project}.txt"
  if [[ ! -f "${assembly}" ]]; then
    printf 'Public API assembly is missing: %s\n' "${assembly}" >&2
    exit 1
  fi
  if [[ ! -f "${baseline}" ]]; then
    printf 'Public API baseline is missing: %s\n' "${baseline}" >&2
    exit 1
  fi
  "${DOTNET_CMD}" run --project "${ROOT_DIR}/scripts/BGCS.ApiSnapshot/BGCS.ApiSnapshot.csproj" \
    --configuration "${CONFIGURATION}" --no-build -- "${assembly}" "${current}"
  if ! diff -u "${baseline}" "${current}"; then
    printf 'Public API changed for %s. Review the change and update the pre-release baseline deliberately.\n' "${project}" >&2
    exit 1
  fi
done

printf '[public-api] %s reviewed assembly baselines are unchanged.\n' "${#projects[@]}"
