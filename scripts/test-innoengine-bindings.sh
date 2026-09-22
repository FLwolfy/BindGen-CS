#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
CONFIGURATION="${CONFIGURATION:-Release}"
INNOENGINE_ROOT="${INNOENGINE_ROOT:-$(cd "${ROOT_DIR}/.." && pwd)/InnoEngine}"
WORKSPACE_CONFIG="${INNOENGINE_ROOT}/native/bindings/workspace.json"
NATIVE_CONFIGURATION="$(printf '%s' "${CONFIGURATION}" | tr '[:upper:]' '[:lower:]')"

if [[ ! -f "${WORKSPACE_CONFIG}" ]]; then
  printf '[inno-bindings] Missing workspace configuration: %s\n' "${WORKSPACE_CONFIG}" >&2
  exit 1
fi

printf '[inno-bindings] Verify deterministic generation for %s.\n' "${WORKSPACE_CONFIG}"
"${DOTNET_CMD}" run \
  --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" \
  --configuration "${CONFIGURATION}" \
  --no-build \
  -- workspace diff "${WORKSPACE_CONFIG}"

if command -v rg > /dev/null 2>&1; then
  manual_imports="$(rg -n \
    --glob '*.cs' \
    --glob '!**/Generated/**' \
    --glob '!**/bin/**' \
    --glob '!**/obj/**' \
    '\[(DllImport|LibraryImport)\b|partial\s+.*\bextern\b|static\s+extern\b' \
    "${INNOENGINE_ROOT}/native" || true)"
  if [[ -n "${manual_imports}" ]]; then
    printf '[inno-bindings] Hand-authored native imports remain outside Generated/:\n%s\n' "${manual_imports}" >&2
    exit 1
  fi
else
  printf '[inno-bindings] ripgrep is required to audit hand-authored native imports.\n' >&2
  exit 1
fi

printf '[inno-bindings] Restore the complete InnoEngine solution.\n'
"${DOTNET_CMD}" restore "${INNOENGINE_ROOT}/InnoEngine.sln" --nologo

run_native_build() {
  local project="$1"
  shift
  printf '[inno-bindings] Build native dependency with %s.\n' "${project}"
  "${DOTNET_CMD}" run \
    --project "${INNOENGINE_ROOT}/${project}" \
    --configuration "${CONFIGURATION}" \
    --no-restore \
    -p:TreatWarningsAsErrors=true \
    -- "$@"
}

printf '[inno-bindings] Build every native dependency required by the generated bindings.\n'
run_native_build "build/toolchains/Inno.Build.Toolchains.Sdl3/Inno.Build.Toolchains.Sdl3.csproj" build --config "${NATIVE_CONFIGURATION}"
run_native_build "build/toolchains/Inno.Build.Toolchains.MiniAudio/Inno.Build.Toolchains.MiniAudio.csproj" build --config "${NATIVE_CONFIGURATION}"
run_native_build "build/toolchains/Inno.Build.Toolchains.ImGui/Inno.Build.Toolchains.ImGui.csproj" build --config "${NATIVE_CONFIGURATION}"
run_native_build "build/toolchains/Inno.Build.Toolchains.ImGuizmo/Inno.Build.Toolchains.ImGuizmo.csproj" build --config "${NATIVE_CONFIGURATION}"
run_native_build "build/toolchains/Inno.Build.Toolchains.Bgfx/Inno.Build.Toolchains.Bgfx.csproj" native --config "${NATIVE_CONFIGURATION}"
run_native_build "build/toolchains/Inno.Build.Toolchains.Bgfx/Inno.Build.Toolchains.Bgfx.csproj" tools --config "${NATIVE_CONFIGURATION}"

printf '[inno-bindings] Build the complete InnoEngine solution.\n'
"${DOTNET_CMD}" build "${INNOENGINE_ROOT}/InnoEngine.sln" \
  --configuration "${CONFIGURATION}" \
  --no-restore \
  --no-incremental \
  -p:TreatWarningsAsErrors=true \
  --nologo

printf '[inno-bindings] Run every native binding test project.\n'
test_count=0
while IFS= read -r test_project; do
  test_count=$((test_count + 1))
  "${DOTNET_CMD}" test "${test_project}" \
    --configuration "${CONFIGURATION}" \
    --no-build \
    --no-restore \
    --nologo
done < <(find "${INNOENGINE_ROOT}/tests/native" -type f -name '*.csproj' | sort)

if (( test_count == 0 )); then
  printf '[inno-bindings] No native binding test projects were discovered.\n' >&2
  exit 1
fi

printf '[inno-bindings] Workspace diff, native dependency builds, full solution build, import audit, and %s native test projects passed.\n' "${test_count}"
