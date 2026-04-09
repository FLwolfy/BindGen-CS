#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
SKIP_RESTORE_BUILD="${SKIP_RESTORE_BUILD:-0}"

log() {
  printf '[full-test-matrix] %s\n' "$1"
}

run_tests() {
  local project="$1"
  log "dotnet test ${project}"
  dotnet test "${ROOT_DIR}/${project}" --configuration "${CONFIGURATION}" --no-build
}

discover_test_projects() {
  find "${ROOT_DIR}/tests" -type f -name "*.csproj" \
    ! -path "*/bin/*" \
    ! -path "*/obj/*" \
    | sort
}

if [[ "${SKIP_RESTORE_BUILD}" != "1" ]]; then
  log "dotnet restore BindGen-CS.sln"
  dotnet restore "${ROOT_DIR}/BindGen-CS.sln"

  log "dotnet build BindGen-CS.sln (${CONFIGURATION})"
  dotnet build "${ROOT_DIR}/BindGen-CS.sln" --configuration "${CONFIGURATION}" --no-restore
fi

log "Layer 1: Auto-discovered test projects under tests/"
mapfile -t TEST_PROJECTS < <(discover_test_projects)

if [[ "${#TEST_PROJECTS[@]}" -eq 0 ]]; then
  log "No test projects found under tests/"
  exit 1
fi

for project_path in "${TEST_PROJECTS[@]}"; do
  project_relative="${project_path#${ROOT_DIR}/}"
  run_tests "${project_relative}"
done

DEMO_DIR="${ROOT_DIR}/demo/BGCS.Demo"
DEMO_BIN_DIR="${DEMO_DIR}/bin/${CONFIGURATION}/generated"
RUNTIME_GENERATED_OUT="${DEMO_BIN_DIR}/OutputRuntimeGenerated"
RUNTIME_NOTGENERATED_OUT="${DEMO_BIN_DIR}/OutputRuntimeNotGenerated"

log "Layer 2: End-to-end demo generation checks"
pushd "${DEMO_DIR}" > /dev/null

rm -rf "${DEMO_BIN_DIR}"
mkdir -p "${DEMO_BIN_DIR}"

dotnet run --project BGCS.Demo.csproj --configuration "${CONFIGURATION}" --no-build -- config.runtime-generated.json "${RUNTIME_GENERATED_OUT}"
dotnet run --project BGCS.Demo.csproj --configuration "${CONFIGURATION}" --no-build -- config.runtime-notgenerated.json "${RUNTIME_NOTGENERATED_OUT}"

if [[ ! -f "${RUNTIME_GENERATED_OUT}/Bindings.cs" ]]; then
  log "Expected ${RUNTIME_GENERATED_OUT}/Bindings.cs to exist"
  exit 1
fi

if [[ ! -f "${RUNTIME_GENERATED_OUT}/Runtime.cs" ]]; then
  log "Expected runtime-generated scenario to generate Runtime.cs"
  exit 1
fi

if [[ ! -f "${RUNTIME_NOTGENERATED_OUT}/Bindings.cs" ]]; then
  log "Expected runtime-notgenerated output to contain Bindings.cs"
  exit 1
fi

if [[ -f "${RUNTIME_NOTGENERATED_OUT}/Runtime.cs" ]]; then
  log "Runtime-notgenerated scenario must not generate Runtime.cs"
  exit 1
fi

if ! grep -q "using BGCS.Runtime;" "${RUNTIME_GENERATED_OUT}/Bindings.cs"; then
  log "Runtime-generated bindings must contain using BGCS.Runtime;"
  exit 1
fi

if ! grep -q "namespace BGCS.Runtime" "${RUNTIME_GENERATED_OUT}/Runtime.cs"; then
  log "Runtime.cs must contain namespace BGCS.Runtime"
  exit 1
fi

popd > /dev/null

log "All layers passed."
