#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
CONFIGURATION="${CONFIGURATION:-Release}"
SKIP_RESTORE_BUILD="${SKIP_RESTORE_BUILD:-0}"
GATE_DIR="${ROOT_DIR}/artifacts/acceptance/gates"
rm -rf "${GATE_DIR}"
mkdir -p "${GATE_DIR}"

log() {
  printf '[full-test-matrix] %s\n' "$1"
}

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
    /m:1 \
    /nodeReuse:false

  log "dotnet build BindGen-CS.sln (${CONFIGURATION})"
  "${DOTNET_CMD}" build "${ROOT_DIR}/BindGen-CS.sln" \
    --configuration "${CONFIGURATION}" \
    --no-restore \
    --no-incremental \
    -p:TreatWarningsAsErrors=true \
    -p:BuildInParallel=false \
    /m:1 \
    /nodeReuse:false
fi
touch "${GATE_DIR}/solution-build"

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
"${DOTNET_CMD}" test "${ROOT_DIR}/tests/BGCS.Cpp2C.Tests/BGCS.Cpp2C.Tests.csproj" --configuration "${CONFIGURATION}" --no-build --filter "FullyQualifiedName~Adapter|FullyQualifiedName~VirtualCallback|FullyQualifiedName~TemplateInstantiation|FullyQualifiedName~NonBlittable"
touch "${GATE_DIR}/modern-cpp"

DEMO_DIR="${ROOT_DIR}/demo/BGCS.Demo"
DEMO_BIN_DIR="${DEMO_DIR}/bin/${CONFIGURATION}/generated"
RUNTIME_GENERATED_OUT="${DEMO_BIN_DIR}/OutputRuntimeGenerated"
RUNTIME_NOTGENERATED_OUT="${DEMO_BIN_DIR}/OutputRuntimeNotGenerated"

log "Layer 2: End-to-end demo generation checks"
pushd "${DEMO_DIR}" > /dev/null

rm -rf "${DEMO_BIN_DIR}"
mkdir -p "${DEMO_BIN_DIR}"

"${DOTNET_CMD}" run --project BGCS.Demo.csproj --configuration "${CONFIGURATION}" --no-build -- config.runtime-generated.json "${RUNTIME_GENERATED_OUT}"
"${DOTNET_CMD}" run --project BGCS.Demo.csproj --configuration "${CONFIGURATION}" --no-build -- config.runtime-notgenerated.json "${RUNTIME_NOTGENERATED_OUT}"

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
touch "${GATE_DIR}/demo"

log "Layer 3: Vendored real-library regeneration and compilation"
REQUIRE_REAL_LIBRARIES=1 bash "${ROOT_DIR}/scripts/test-real-libraries.sh"
touch "${GATE_DIR}/real-libraries"
touch "${GATE_DIR}/ir-native-real-libraries"
REQUIRE_REAL_CPP_LIBRARIES=1 bash "${ROOT_DIR}/scripts/test-real-cpp-libraries.sh"
touch "${GATE_DIR}/real-cpp-libraries"

log "Layer 4: Reviewed public API compatibility baseline"
bash "${ROOT_DIR}/scripts/test-public-api-compatibility.sh"
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

log "Layer 8: Machine-readable acceptance report"
bash "${ROOT_DIR}/scripts/write-acceptance-report.sh"

log "All layers passed."
