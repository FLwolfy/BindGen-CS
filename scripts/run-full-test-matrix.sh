#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
CONFIGURATION="${CONFIGURATION:-Release}"
SKIP_RESTORE_BUILD="${SKIP_RESTORE_BUILD:-0}"
# The MSVC developer shell exports Platform=x64. Leaving that ambient value in
# place changes project-level `dotnet test --no-build` output lookup, while
# setting Platform=AnyCPU breaks solution restore (the solution spells it
# "Any CPU"). Let each solution/project use its own default instead.
if [[ "$(detect_snapshot_platform)" == "windows-x64" ]]; then
  unset Platform PLATFORM platform
fi
GATE_DIR="${ROOT_DIR}/artifacts/acceptance/gates"
rm -rf "${GATE_DIR}"
mkdir -p "${GATE_DIR}"

log() {
  printf '[full-test-matrix] %s\n' "$1"
}

log "Snapshot manifest portability and mismatch regression"
bash "${ROOT_DIR}/scripts/test-snapshot-manifest.sh"

run_tests() {
  local project="$1"
  log "dotnet test ${project}"
  "${DOTNET_CMD}" test "${ROOT_DIR}/${project}" --configuration "${CONFIGURATION}" --no-build
}

discover_test_projects() {
  find "${ROOT_DIR}/tests" -type f -name "*.csproj" \
    ! -path "*/bin/*" \
    ! -path "*/obj/*" \
    | sort
}

if [[ "${SKIP_RESTORE_BUILD}" != "1" ]]; then
  log "dotnet restore BindGen-CS.sln"
  "${DOTNET_CMD}" restore "${ROOT_DIR}/BindGen-CS.sln" \
    -p:BuildInParallel=false \
    -m:1 \
    -nodeReuse:false

  log "dotnet build BindGen-CS.sln (${CONFIGURATION})"
  "${DOTNET_CMD}" build "${ROOT_DIR}/BindGen-CS.sln" \
    --configuration "${CONFIGURATION}" \
    --no-restore \
    --no-incremental \
    -p:TreatWarningsAsErrors=true \
    -p:BuildInParallel=false \
    -m:1 \
    -nodeReuse:false
fi
touch "${GATE_DIR}/solution-build"

log "Production source ownership, declaration style and public documentation"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" \
  --configuration "${CONFIGURATION}" --no-build -- validate architecture "${ROOT_DIR}"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" \
  --configuration "${CONFIGURATION}" --no-build -- validate style "${ROOT_DIR}/src"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" \
  --configuration "${CONFIGURATION}" --no-build -- validate documentation "${ROOT_DIR}/src"
touch "${GATE_DIR}/architecture" "${GATE_DIR}/style" "${GATE_DIR}/documentation"

log "Layer 1: Auto-discovered test projects under tests/"
TEST_PROJECTS=()
while IFS= read -r project_path; do
  TEST_PROJECTS+=("${project_path}")
done < <(discover_test_projects)

if [[ "${#TEST_PROJECTS[@]}" -eq 0 ]]; then
  log "No test projects found under tests/"
  exit 1
fi

for project_path in "${TEST_PROJECTS[@]}"; do
  project_relative="${project_path#${ROOT_DIR}/}"
  run_tests "${project_relative}"
done
touch "${GATE_DIR}/managed-tests"
touch "${GATE_DIR}/callback-async-lifetime"
touch "${GATE_DIR}/advanced-cpp-semantics"

if [[ "$(detect_snapshot_platform)" == "windows-x64" ]]; then
  if [[ "${BGCS_REQUIRE_WINDOWS_NATIVE_PROVIDERS:-0}" != "1" ]]; then
    log "Windows x64 acceptance requires BGCS_REQUIRE_WINDOWS_NATIVE_PROVIDERS=1."
    exit 1
  fi
  touch "${GATE_DIR}/windows-native-providers"
fi

"${DOTNET_CMD}" test "${ROOT_DIR}/tests/BGCS.Generation.Tests/BGCS.Generation.Tests.csproj" --configuration "${CONFIGURATION}" --no-build --filter "FullyQualifiedName~WindowsNativeAbi|FullyQualifiedName~NativeAbiTypeConversion"
touch "${GATE_DIR}/native-c-abi"
"${DOTNET_CMD}" test "${ROOT_DIR}/tests/BGCS.Generation.Tests/BGCS.Generation.Tests.csproj" --configuration "${CONFIGURATION}" --no-build --filter "FullyQualifiedName~StrictSafety"
touch "${GATE_DIR}/strict-safety"
"${DOTNET_CMD}" test "${ROOT_DIR}/tests/BGCS.Cpp2C.Tests/BGCS.Cpp2C.Tests.csproj" --configuration "${CONFIGURATION}" --no-build --filter "FullyQualifiedName~LinkAndInvokeNativeDll"
touch "${GATE_DIR}/native-cpp-bridge"
"${DOTNET_CMD}" test "${ROOT_DIR}/tests/BGCS.Cpp2C.Tests/BGCS.Cpp2C.Tests.csproj" --configuration "${CONFIGURATION}" --no-build --filter "FullyQualifiedName~Lowering|FullyQualifiedName~VirtualCallback|FullyQualifiedName~TemplateInstantiation|FullyQualifiedName~NonBlittable"
touch "${GATE_DIR}/modern-cpp"

EXAMPLE_DIR="${ROOT_DIR}/examples/QuickStart"
EXAMPLE_OUT="${ROOT_DIR}/artifacts/examples/QuickStart/${CONFIGURATION}"
RUNTIME_GENERATED_OUT="${EXAMPLE_OUT}/standalone"
RUNTIME_NOTGENERATED_OUT="${EXAMPLE_OUT}/packaged-runtime"

log "Layer 2: Public CLI example generation and compilation"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool" --configuration "${CONFIGURATION}" --no-build -- build "${EXAMPLE_DIR}/standalone.json" --output "${RUNTIME_GENERATED_OUT}"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool" --configuration "${CONFIGURATION}" --no-build -- build "${EXAMPLE_DIR}/bindgen.json" --output "${RUNTIME_NOTGENERATED_OUT}"

for bindings in "${RUNTIME_GENERATED_OUT}/Bindings.cs" "${RUNTIME_NOTGENERATED_OUT}/Bindings.cs"; do
  if [[ ! -f "${bindings}" ]]; then
    log "Expected generated binding is missing: ${bindings}"
    exit 1
  fi
done
if [[ ! -f "${RUNTIME_GENERATED_OUT}/Runtime.cs" || -f "${RUNTIME_NOTGENERATED_OUT}/Runtime.cs" ]]; then
  log "Runtime source ownership differs from the selected example configuration."
  exit 1
fi
touch "${GATE_DIR}/examples"

log "Layer 3: Pinned upstream real-library regeneration and compilation"
bash "${ROOT_DIR}/scripts/setup-real-library-corpus.sh"
snapshot_status=0
if REQUIRE_REAL_LIBRARIES=1 bash "${ROOT_DIR}/scripts/test-real-libraries.sh"; then
  touch "${GATE_DIR}/real-libraries" "${GATE_DIR}/ir-native-real-libraries"
else
  status=$?
  if [[ "${status}" != "3" ]]; then exit "${status}"; fi
  snapshot_status=3
fi
if REQUIRE_REAL_CPP_LIBRARIES=1 bash "${ROOT_DIR}/scripts/test-real-cpp-libraries.sh"; then
  touch "${GATE_DIR}/real-cpp-libraries"
else
  status=$?
  if [[ "${status}" != "3" ]]; then exit "${status}"; fi
  snapshot_status=3
fi
log "Layer 4: Reviewed public API compatibility baseline"
bash "${ROOT_DIR}/scripts/test-public-api-snapshot.sh"
touch "${GATE_DIR}/api-compatibility"

log "Layer 5: NuGet package, tool, and native RID consumer smoke tests"
bash "${ROOT_DIR}/scripts/test-nuget-packages.sh"
touch "${GATE_DIR}/nuget-tool"
touch "${GATE_DIR}/native-package"

log "Layer 6: Performance and cache budget"
bash "${ROOT_DIR}/scripts/test-performance-budget.sh"
touch "${GATE_DIR}/performance"

log "Layer 7: License and vulnerability policy"
bash "${ROOT_DIR}/scripts/test-supply-chain-policy.sh"
touch "${GATE_DIR}/supply-chain"

if [[ "${snapshot_status}" != "0" ]]; then
  log "Unreviewed host snapshot candidates were captured under artifacts/acceptance/candidate-snapshots; acceptance remains failed."
  exit "${snapshot_status}"
fi

log "Layer 8: Machine-readable acceptance report"
bash "${ROOT_DIR}/scripts/write-acceptance-report.sh"

log "All layers passed."
