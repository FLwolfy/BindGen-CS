#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
CONFIGURATION="${CONFIGURATION:-Release}"
BASELINE_DIR="${ROOT_DIR}/tests/public-api"
CURRENT_DIR="${ROOT_DIR}/artifacts/public-api"
mkdir -p "${CURRENT_DIR}"

projects=()
while IFS= read -r project; do
  project="${project%$'\r'}"
  projects+=("${project}")
done < <(
  "${PYTHON:-python3}" -c 'import json,sys; m=json.load(open(sys.argv[1])); rows=[name+"|"+m["assemblyProjects"][name] for name in m["publicApiAssemblies"]]; print("\n".join(rows))' \
    "${ROOT_DIR}/distribution/manifests/product-packages.json"
)
if (( ${#projects[@]} == 0 )); then
  printf 'The delivered public API assembly manifest is empty.\n' >&2
  exit 1
fi
if [[ "${SKIP_RESTORE_BUILD:-0}" != "1" ]]; then
  "${DOTNET_CMD}" build "${ROOT_DIR}/BindGen-CS.slnx" --configuration "${CONFIGURATION}"
  "${DOTNET_CMD}" build "${ROOT_DIR}/eng/BGCS.RepositoryTools/BGCS.RepositoryTools.csproj" --configuration "${CONFIGURATION}"
fi

for record in "${projects[@]}"; do
  IFS="|" read -r project project_directory <<< "${record}"
  assembly="${ROOT_DIR}/${project_directory}/bin/${CONFIGURATION}/net10.0/${project}.dll"
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
  "${DOTNET_CMD}" run --project "${ROOT_DIR}/eng/BGCS.RepositoryTools/BGCS.RepositoryTools.csproj" \
    --configuration "${CONFIGURATION}" --no-build -- validate api-snapshot "${assembly}" "${current}"
  if ! diff -u "${baseline}" "${current}"; then
    printf 'Public API changed for %s. Review the change and update the pre-release baseline deliberately.\n' "${project}" >&2
    exit 1
  fi
done

printf '[public-api] %s reviewed assembly baselines are unchanged.\n' "${#projects[@]}"
