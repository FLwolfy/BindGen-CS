#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
PYTHON_CMD="${PYTHON:-python3}"
CONFIGURATION="${CONFIGURATION:-Release}"
SKIP_RESTORE_BUILD="${SKIP_RESTORE_BUILD:-0}"
# The MSVC developer shell exports Platform=x64. Leaving that ambient value in
# place changes project-level `dotnet test --no-build` output lookup, while
# setting Platform=AnyCPU breaks solution restore (the solution spells it
# "Any CPU"). Let each solution/project use its own default instead.
if [[ "$(detect_snapshot_platform)" == "windows-x64" ]]; then
  unset Platform PLATFORM platform
fi
# Test fixtures select their SDK and header search inputs explicitly. The MSVC
# action's ambient injection paths must not silently disable the cache cases.
# Keep compiler discovery in PATH and the VC/Windows SDK selectors available.
unset INCLUDE CPATH C_INCLUDE_PATH CPLUS_INCLUDE_PATH OBJC_INCLUDE_PATH SDKROOT
unset GCC_EXEC_PREFIX COMPILER_PATH LIBRARY_PATH CL _CL_ LINK _LINK_ LIB CCC_OVERRIDE_OPTIONS
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-$$"
GATE_DIR="${ROOT_DIR}/artifacts/acceptance/gates/${RUN_ID}"
TEST_DIR="${ROOT_DIR}/artifacts/acceptance/test-results/${RUN_ID}"
export BGCS_ACCEPTANCE_GATE_DIR="${GATE_DIR}"
export BGCS_ACCEPTANCE_TEST_ROOT="${TEST_DIR}"
mkdir -p "${GATE_DIR}" "${TEST_DIR}"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/source_ownership.py" "${ROOT_DIR}"
start_arguments=(start "${ROOT_DIR}" --output "${GATE_DIR}/run-start.json")
if [[ "${SKIP_RESTORE_BUILD}" == "1" ]]; then start_arguments+=(--uses-prebuilt-outputs); fi
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/acceptance_evidence.py" "${start_arguments[@]}"

log() {
  printf '[full-test-matrix] %s\n' "$1"
}

log "Snapshot manifest portability and mismatch regression"
bash "${ROOT_DIR}/scripts/test-snapshot-manifest.sh"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/generate-diagnostic-docs.py" --check
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-package-evidence.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-package-closures.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-source-ownership.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-tool-worker-publish.py" --dotnet "${DOTNET_CMD}"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-parser-resource-provenance.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-feature-release-evidence.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-readiness-review-candidates.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-scoped-acceptance-removals.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-global-feature-evidence.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-test-case-evidence.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-release-payload.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-compiler-test-environment.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-locked-dotnet-bootstrap.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-locked-workload-manifests.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-disk-budget.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-failed-performance-profile.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-published-product-evidence.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-corpus-snapshots.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-native-fixture-build.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-corpus-bridge.py"

run_tests() {
  local project="$1"
  local name="$(basename "${project}" .csproj)"
  log "dotnet test ${project}"
  "${DOTNET_CMD}" test "${ROOT_DIR}/${project}" --configuration "${CONFIGURATION}" --no-build \
    --logger "trx;LogFileName=${name}.trx" --results-directory "${TEST_DIR}"
  "${PYTHON_CMD}" "${ROOT_DIR}/scripts/acceptance_evidence.py" tests "${TEST_DIR}/${name}.trx" \
    --output "${TEST_DIR}/${name}.json"
}

discover_test_projects() {
  "${PYTHON_CMD}" "${ROOT_DIR}/scripts/acceptance_evidence.py" discover-tests "${ROOT_DIR}"
}

require_test_evidence() {
  local project="$1" pattern="$2" gate="$3"
  "${PYTHON_CMD}" "${ROOT_DIR}/scripts/acceptance_evidence.py" tests "${TEST_DIR}/${project}.trx" \
    --match "${pattern}" --output "${TEST_DIR}/${gate}.matched.json"
}

if [[ "${SKIP_RESTORE_BUILD}" != "1" ]]; then
  log "dotnet restore BindGen-CS.slnx"
  "${DOTNET_CMD}" restore "${ROOT_DIR}/BindGen-CS.slnx" \
    -p:BuildInParallel=false \
    -m:1 \
    -nodeReuse:false

  log "dotnet build BindGen-CS.slnx (${CONFIGURATION})"
  "${DOTNET_CMD}" build "${ROOT_DIR}/BindGen-CS.slnx" \
    --configuration "${CONFIGURATION}" \
    --no-restore \
    --no-incremental \
    -p:TreatWarningsAsErrors=true \
    -p:BuildInParallel=false \
    -m:1 \
    -nodeReuse:false
fi
touch "${GATE_DIR}/solution-build"
"${DOTNET_CMD}" build "${ROOT_DIR}/eng/BGCS.TestDriver/BGCS.TestDriver.csproj" --configuration "${CONFIGURATION}" -m:1 -nr:false -p:TreatWarningsAsErrors=true
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-host-test-driver.py" --dotnet "${DOTNET_CMD}" --configuration "${CONFIGURATION}"

log "Production source ownership, declaration style and public documentation"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/eng/BGCS.RepositoryTools/BGCS.RepositoryTools.csproj" \
  --configuration "${CONFIGURATION}" --no-build -- validate architecture "${ROOT_DIR}"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/eng/BGCS.RepositoryTools/BGCS.RepositoryTools.csproj" \
  --configuration "${CONFIGURATION}" --no-build -- validate style "${ROOT_DIR}/src"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/eng/BGCS.RepositoryTools/BGCS.RepositoryTools.csproj" \
  --configuration "${CONFIGURATION}" --no-build -- validate documentation "${ROOT_DIR}/src"
touch "${GATE_DIR}/architecture" "${GATE_DIR}/style" "${GATE_DIR}/documentation"

log "Strict performance and cache budget before the long native-consumer baseline"
bash "${ROOT_DIR}/scripts/test-performance-budget.sh"
touch "${GATE_DIR}/performance"

log "Pinned real-library generation, compilation and snapshots before the long native-consumer baseline"
bash "${ROOT_DIR}/scripts/setup-real-library-corpus.sh"
if "${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-corpus.py" --dotnet "${DOTNET_CMD}" \
  --configuration "${CONFIGURATION}" --collect-groups; then
  touch "${GATE_DIR}/real-libraries" "${GATE_DIR}/ir-native-real-libraries" "${GATE_DIR}/real-cpp-libraries"
else
  snapshot_status=$?
  if [[ "${snapshot_status}" == "3" ]]; then
    log "Unreviewed host snapshot candidates were captured for review; acceptance remains failed."
  fi
  exit "${snapshot_status}"
fi

log "Layer 1: Auto-discovered test projects under tests/"
TEST_PROJECTS=()
while IFS= read -r project_path; do
  project_path="${project_path%$'\r'}"
  TEST_PROJECTS+=("${project_path}")
done < <(discover_test_projects)

if [[ "${#TEST_PROJECTS[@]}" -eq 0 ]]; then
  log "No test projects found under tests/"
  exit 1
fi

for project_path in "${TEST_PROJECTS[@]}"; do
  project_relative="${project_path#${ROOT_DIR}/}"
  if [[ "${project_relative}" == "tests/BGCS.Tests/BGCS.Tests.csproj" ]]; then
    "${PYTHON_CMD}" "${ROOT_DIR}/scripts/run-host-tests.py" --dotnet "${DOTNET_CMD}" --configuration "${CONFIGURATION}" --output "${TEST_DIR}"
  else
    run_tests "${project_relative}"
  fi
done
touch "${GATE_DIR}/managed-tests"
require_test_evidence BGCS.Runtime.Tests 'CallbackLifetimeTests' retained-callback-lifetime
require_test_evidence BGCS.Runtime.Tests 'AsyncLifetimeTests' asynchronous-operation-lifetime
require_test_evidence BGCS.Runtime.Tests 'ModuleLeaseTests|HandleLifetimeTests' module-handle-lifetime
touch "${GATE_DIR}/callback-async-lifetime"

# The repository fixture builds its native consumer directly; this is not a BGCS provider service.
if [[ "$(detect_snapshot_platform)" == "windows-x64" ]]; then
  require_test_evidence BGCS.Tests 'NativeMsvcStandardLibraryTests.*MsvcBuildsAndInvokesUserClassWithStringVectorAndOptionalStorage' windows-native-consumer
fi

require_test_evidence BGCS.Tests 'NativeWideTextTests' native-wide-text
require_test_evidence BGCS.Tests 'NativeBitfieldTests' native-bitfields
require_test_evidence BGCS.Tests 'NativeBufferTests' native-buffers
require_test_evidence BGCS.Tests 'NativeManagedEnumTests' native-managed-enums
touch "${GATE_DIR}/native-c-abi"
require_test_evidence BGCS.CSharp.Tests 'NeutralBackendTests' strict-abi-planning
require_test_evidence BGCS.CSharp.Tests 'BufferProjectionTests' strict-buffer-projection
require_test_evidence BGCS.CSharp.Tests 'OwnedProjectionTests|TextOwnershipProjectionTests' strict-ownership-projection
touch "${GATE_DIR}/strict-safety"
require_test_evidence BGCS.Tests 'NeutralCppBridgeTests|NativeDefaultConstructionTests' native-cpp-bridge
touch "${GATE_DIR}/native-cpp-bridge"
require_test_evidence BGCS.Tests 'NativeStandardLoweringTests' native-standard-lowerings
require_test_evidence BGCS.Tests 'NativeTemplateBindingTests' native-template-instantiations
require_test_evidence BGCS.Tests 'NativeVirtualCallbackTests' native-virtual-callbacks
require_test_evidence BGCS.Tests 'NativeCppSequenceTests' native-sequence-adapters
require_test_evidence BGCS.Tests 'NativeShimInheritanceTests' native-shim-inheritance
touch "${GATE_DIR}/modern-cpp" "${GATE_DIR}/advanced-cpp-semantics"

log "Layer 2: Public CLI example generation, exact manifest compilation and native invocation"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-ci-product-evidence.py"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-cli-examples.py" --dotnet "${DOTNET_CMD}" \
  --configuration "${CONFIGURATION}" --output "${ROOT_DIR}/artifacts/acceptance/examples.json"
touch "${GATE_DIR}/examples"

log "Layer 2b: Real custom allocator, matching native frees and retained module lifetime"
"${PYTHON_CMD}" "${ROOT_DIR}/scripts/test-custom-allocator.py" --dotnet "${DOTNET_CMD}" \
  --configuration "${CONFIGURATION}" --output "${ROOT_DIR}/artifacts/acceptance/custom-allocator.json"
touch "${GATE_DIR}/custom-allocator"

log "Layer 4: Reviewed public API compatibility baseline"
SKIP_RESTORE_BUILD=1 bash "${ROOT_DIR}/scripts/test-public-api-snapshot.sh"
touch "${GATE_DIR}/api-compatibility"

log "Layer 7: License and vulnerability policy"
bash "${ROOT_DIR}/scripts/test-supply-chain-policy.sh"
touch "${GATE_DIR}/supply-chain"

log "Layer 8: Machine-readable acceptance report"
bash "${ROOT_DIR}/scripts/write-acceptance-report.sh"

log "All layers passed."
