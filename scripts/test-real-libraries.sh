#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
CORPUS_ROOT="${BGCS_REAL_LIBRARY_ROOT:-${ROOT_DIR}/artifacts/real-library-corpus}"
ARTIFACTS_DIR="${ROOT_DIR}/artifacts/real-libraries"
MINIAUDIO_HEADER="${CORPUS_ROOT}/miniaudio/extras/miniaudio_split/miniaudio.h"
SDL3_HEADER="${CORPUS_ROOT}/SDL/include/SDL3/SDL.h"
CIMGUI_HEADER="${CORPUS_ROOT}/cimgui/cimgui.h"
CIMGUIZMO_HEADER="${CORPUS_ROOT}/cimguizmo/cimguizmo.h"
BGFX_HEADER="${CORPUS_ROOT}/bgfx/include/bgfx/c99/bgfx.h"
REQUIRE_REAL_LIBRARIES="${REQUIRE_REAL_LIBRARIES:-0}"

check_generation_budget() {
  local library_name="$1"
  local elapsed_seconds="$2"
  local budget_seconds="$3"
  if [[ "${BGCS_ENFORCE_GENERATION_BUDGETS:-0}" == "1" ]] && (( elapsed_seconds > budget_seconds )); then
    echo "[real-libraries] ${library_name} generation exceeded ${budget_seconds} seconds: ${elapsed_seconds}s"
    exit 1
  fi
}

if [[ ! -f "${MINIAUDIO_HEADER}" || ! -f "${SDL3_HEADER}" || ! -f "${CIMGUI_HEADER}" || ! -f "${CIMGUIZMO_HEADER}" || ! -f "${BGFX_HEADER}" ]]; then
  if [[ "${REQUIRE_REAL_LIBRARIES}" == "1" ]]; then
    echo "Required real-library headers were not found under: ${CORPUS_ROOT}"
    exit 1
  fi
  echo "[real-libraries] Independent test corpus not found; run scripts/setup-real-library-corpus.sh."
  exit 0
fi

rm -rf "${ARTIFACTS_DIR}"
mkdir -p "${ARTIFACTS_DIR}/miniaudio/consumer"
if command -v cygpath > /dev/null 2>&1; then
  MINIAUDIO_HEADER_JSON="$(cygpath -m "${MINIAUDIO_HEADER}")"
  SDL3_HEADER_JSON="$(cygpath -m "${SDL3_HEADER}")"
  CIMGUI_HEADER_JSON="$(cygpath -m "${CIMGUI_HEADER}")"
  CIMGUIZMO_HEADER_JSON="$(cygpath -m "${CIMGUIZMO_HEADER}")"
  BGFX_HEADER_JSON="$(cygpath -m "${BGFX_HEADER}")"
  ROOT_DIR_JSON="$(cygpath -m "${ROOT_DIR}")"
else
  MINIAUDIO_HEADER_JSON="${MINIAUDIO_HEADER}"
  SDL3_HEADER_JSON="${SDL3_HEADER}"
  CIMGUI_HEADER_JSON="${CIMGUI_HEADER}"
  CIMGUIZMO_HEADER_JSON="${CIMGUIZMO_HEADER}"
  BGFX_HEADER_JSON="${BGFX_HEADER}"
  ROOT_DIR_JSON="${ROOT_DIR}"
fi
MINIAUDIO_INCLUDE_JSON="$(dirname "${MINIAUDIO_HEADER_JSON}")"
SDL3_INCLUDE_JSON="$(dirname "$(dirname "${SDL3_HEADER_JSON}")")"
CIMGUI_INCLUDE_JSON="$(dirname "${CIMGUI_HEADER_JSON}")"
CIMGUIZMO_INCLUDE_JSON="$(dirname "${CIMGUIZMO_HEADER_JSON}")"
BGFX_INCLUDE_JSON="$(dirname "$(dirname "${BGFX_HEADER_JSON}")")"
BX_INCLUDE_JSON="${CORPUS_ROOT}/bx/include"
if command -v cygpath > /dev/null 2>&1; then BX_INCLUDE_JSON="$(cygpath -m "${BX_INCLUDE_JSON}")"; fi

"${DOTNET_CMD}" build "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release -m:1 -nodeReuse:false

cat > "${ARTIFACTS_DIR}/miniaudio/bindgen.json" <<EOF
{
  "preset": "host-c,c-library",
  "strictSafetySeverity": "Warning",
  "namespace": "BGCS.RealLibraries.MiniAudio",
  "apiName": "MiniAudio",
  "libName": "miniaudio",
  "entryFiles": ["${MINIAUDIO_HEADER_JSON}"],
  "allowedHeaders": [],
  "includeTransitivelyReferencedHeaders": true,
  "includeFolders": ["${MINIAUDIO_INCLUDE_JSON}"],
  "ignoredFunctions": ["ma_log_postf"],
  "outputPath": "Generated"
}
EOF

start_seconds="$(date +%s)"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  generate "${ARTIFACTS_DIR}/miniaudio/bindgen.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
check_generation_budget miniaudio "${elapsed_seconds}" 60

cat > "${ARTIFACTS_DIR}/miniaudio/consumer/MiniAudio.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Generated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF

"${DOTNET_CMD}" build "${ARTIFACTS_DIR}/miniaudio/consumer/MiniAudio.Generated.csproj" --configuration Release -m:1 -nodeReuse:false
printf '[real-libraries] miniaudio passed in %ss.\n' "${elapsed_seconds}"

mkdir -p "${ARTIFACTS_DIR}/sdl3/consumer"
cat > "${ARTIFACTS_DIR}/sdl3/bindgen.json" <<EOF
{
  "preset": "host-c,c-library,opaque-callbacks",
  "strictSafetySeverity": "Warning",
  "namespace": "BGCS.RealLibraries.SDL3",
  "apiName": "SDL3",
  "libName": "SDL3",
  "entryFiles": ["${SDL3_HEADER_JSON}"],
  "allowedHeaders": [],
  "includeTransitivelyReferencedHeaders": true,
  "includeFolders": ["${SDL3_INCLUDE_JSON}"],
  "typeMappings": {
    "uint8": "byte", "uint16": "ushort", "uint32": "uint", "uint64": "ulong",
    "sint8": "sbyte", "sint16": "short", "sint32": "int", "sint64": "long"
  },
  "ignoredFunctions": [
    "SDL_sscanf",
    "SDL_snprintf",
    "SDL_swprintf",
    "SDL_asprintf",
    "SDL_SetError",
    "SDL_IOprintf",
    "SDL_Log",
    "SDL_LogTrace",
    "SDL_LogVerbose",
    "SDL_LogDebug",
    "SDL_LogInfo",
    "SDL_LogWarn",
    "SDL_LogError",
    "SDL_LogCritical",
    "SDL_LogMessage",
    "SDL_RenderDebugTextFormat"
  ],
  "outputPath": "Generated"
}
EOF

start_seconds="$(date +%s)"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  generate "${ARTIFACTS_DIR}/sdl3/bindgen.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
check_generation_budget SDL3 "${elapsed_seconds}" 45

cat > "${ARTIFACTS_DIR}/sdl3/consumer/SDL3.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Generated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF

"${DOTNET_CMD}" build "${ARTIFACTS_DIR}/sdl3/consumer/SDL3.Generated.csproj" --configuration Release -m:1 -nodeReuse:false
printf '[real-libraries] SDL3 passed in %ss.\n' "${elapsed_seconds}"

mkdir -p "${ARTIFACTS_DIR}/cimgui/consumer"
cat > "${ARTIFACTS_DIR}/cimgui/bindgen.json" <<EOF
{
  "preset": "host-c,c-library,opaque-callbacks",
  "strictSafetySeverity": "Warning",
  "namespace": "BGCS.RealLibraries.CImGui",
  "apiName": "CImGui",
  "libName": "cimgui",
  "entryFiles": ["${CIMGUI_HEADER_JSON}"],
  "allowedHeaders": [],
  "includeTransitivelyReferencedHeaders": true,
  "includeFolders": ["${CIMGUI_INCLUDE_JSON}"],
  "defines": ["CIMGUI_DEFINE_ENUMS_AND_STRUCTS"],
  "ignoredFunctions": [
    "igText",
    "igTextColored",
    "igTextDisabled",
    "igTextWrapped",
    "igLabelText",
    "igBulletText",
    "igTreeNode_StrStr",
    "igTreeNode_Ptr",
    "igTreeNodeEx_StrStr",
    "igTreeNodeEx_Ptr",
    "igSetTooltip",
    "igSetItemTooltip",
    "igLogText",
    "igDebugLog",
    "igImFormatString",
    "igImFormatStringToTempBuffer",
    "igTextAligned",
    "ImGuiTextBuffer_appendf"
  ],
  "outputPath": "Generated"
}
EOF

start_seconds="$(date +%s)"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  generate "${ARTIFACTS_DIR}/cimgui/bindgen.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
check_generation_budget cimgui "${elapsed_seconds}" 30

cat > "${ARTIFACTS_DIR}/cimgui/consumer/CImGui.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Generated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF

"${DOTNET_CMD}" build "${ARTIFACTS_DIR}/cimgui/consumer/CImGui.Generated.csproj" --configuration Release -m:1 -nodeReuse:false
printf '[real-libraries] cimgui passed in %ss.\n' "${elapsed_seconds}"

mkdir -p "${ARTIFACTS_DIR}/cimguizmo/consumer"
cat > "${ARTIFACTS_DIR}/cimguizmo/bindgen.json" <<EOF
{
  "preset": "host-c,c-library,opaque-callbacks",
  "strictSafetySeverity": "Warning",
  "namespace": "BGCS.RealLibraries.CImGuizmo",
  "apiName": "CImGuizmo",
  "libName": "cimguizmo",
  "entryFiles": ["${CIMGUIZMO_HEADER_JSON}"],
  "allowedHeaders": [],
  "includeTransitivelyReferencedHeaders": true,
  "includeFolders": ["${CIMGUIZMO_INCLUDE_JSON}", "${CIMGUI_INCLUDE_JSON}"],
  "defines": ["CIMGUI_DEFINE_ENUMS_AND_STRUCTS"],
  "ignoredFunctions": [
    "igText",
    "igTextColored",
    "igTextDisabled",
    "igTextWrapped",
    "igLabelText",
    "igBulletText",
    "igTreeNode_StrStr",
    "igTreeNode_Ptr",
    "igTreeNodeEx_StrStr",
    "igTreeNodeEx_Ptr",
    "igSetTooltip",
    "igSetItemTooltip",
    "igLogText",
    "igDebugLog",
    "igImFormatString",
    "igImFormatStringToTempBuffer",
    "igTextAligned",
    "ImGuiTextBuffer_appendf"
  ],
  "outputPath": "Generated"
}
EOF

start_seconds="$(date +%s)"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  generate "${ARTIFACTS_DIR}/cimguizmo/bindgen.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
check_generation_budget cimguizmo "${elapsed_seconds}" 15

cat > "${ARTIFACTS_DIR}/cimguizmo/consumer/CImGuizmo.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Generated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF

"${DOTNET_CMD}" build "${ARTIFACTS_DIR}/cimguizmo/consumer/CImGuizmo.Generated.csproj" --configuration Release -m:1 -nodeReuse:false
printf '[real-libraries] cimguizmo passed in %ss.\n' "${elapsed_seconds}"

mkdir -p "${ARTIFACTS_DIR}/bgfx/consumer"
cat > "${ARTIFACTS_DIR}/bgfx/bindgen.json" <<EOF
{
  "preset": "host-c,c-library,opaque-callbacks",
  "strictSafetySeverity": "Warning",
  "namespace": "BGCS.RealLibraries.Bgfx",
  "apiName": "Bgfx",
  "libName": "bgfx",
  "entryFiles": ["${BGFX_HEADER_JSON}"],
  "allowedHeaders": [],
  "includeTransitivelyReferencedHeaders": true,
  "includeFolders": ["${BGFX_INCLUDE_JSON}", "${BX_INCLUDE_JSON}"],
  "ignoredFunctions": ["bgfx_dbg_text_printf"],
  "outputPath": "Generated"
}
EOF
start_seconds="$(date +%s)"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  generate "${ARTIFACTS_DIR}/bgfx/bindgen.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
check_generation_budget bgfx "${elapsed_seconds}" 30
cat > "${ARTIFACTS_DIR}/bgfx/consumer/Bgfx.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Generated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF
"${DOTNET_CMD}" build "${ARTIFACTS_DIR}/bgfx/consumer/Bgfx.Generated.csproj" --configuration Release -m:1 -nodeReuse:false
printf '[real-libraries] bgfx passed in %ss.\n' "${elapsed_seconds}"

"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  validate api-snapshot   "${ARTIFACTS_DIR}/miniaudio/consumer/bin/Release/net9.0/MiniAudio.Generated.dll" "${ARTIFACTS_DIR}/miniaudio/public-api.txt"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  validate api-snapshot   "${ARTIFACTS_DIR}/sdl3/consumer/bin/Release/net9.0/SDL3.Generated.dll" "${ARTIFACTS_DIR}/sdl3/public-api.txt"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  validate api-snapshot   "${ARTIFACTS_DIR}/cimgui/consumer/bin/Release/net9.0/CImGui.Generated.dll" "${ARTIFACTS_DIR}/cimgui/public-api.txt"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  validate api-snapshot   "${ARTIFACTS_DIR}/cimguizmo/consumer/bin/Release/net9.0/CImGuizmo.Generated.dll" "${ARTIFACTS_DIR}/cimguizmo/public-api.txt"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  validate api-snapshot   "${ARTIFACTS_DIR}/bgfx/consumer/bin/Release/net9.0/Bgfx.Generated.dll" "${ARTIFACTS_DIR}/bgfx/public-api.txt"

pushd "${ROOT_DIR}" > /dev/null
snapshot_status=0
if verify_or_capture_snapshot_manifest "tests/real-libraries/api-snapshots" \
  artifacts/real-libraries/{miniaudio,sdl3,cimgui,cimguizmo,bgfx}/Generated/Bindings.cs; then
  :
else
  status=$?
  if [[ "${status}" != "3" ]]; then exit "${status}"; fi
  snapshot_status=3
fi
if verify_or_capture_snapshot_manifest "tests/real-libraries/public-api-snapshots" \
  artifacts/real-libraries/{miniaudio,sdl3,cimgui,cimguizmo,bgfx}/public-api.txt; then
  :
else
  status=$?
  if [[ "${status}" != "3" ]]; then exit "${status}"; fi
  snapshot_status=3
fi
popd > /dev/null
if [[ "${snapshot_status}" != "0" ]]; then exit "${snapshot_status}"; fi
echo "[real-libraries] deterministic source and public API snapshots passed for $(detect_snapshot_platform)."
