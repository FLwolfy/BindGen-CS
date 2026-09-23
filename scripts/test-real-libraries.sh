#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
INNOENGINE_ROOT="${INNOENGINE_ROOT:-$(cd "${ROOT_DIR}/.." && pwd)/InnoEngine}"
ARTIFACTS_DIR="${ROOT_DIR}/artifacts/real-libraries"
MINIAUDIO_HEADER="${INNOENGINE_ROOT}/extern/miniaudio/extras/miniaudio_split/miniaudio.h"
SDL3_HEADER="${INNOENGINE_ROOT}/extern/SDL/include/SDL3/SDL.h"
CIMGUI_HEADER="${INNOENGINE_ROOT}/extern/cimgui/cimgui.h"
CIMGUIZMO_HEADER="${INNOENGINE_ROOT}/extern/cimguizmo/cimguizmo.h"
BGFX_HEADER="${INNOENGINE_ROOT}/extern/bgfx/include/bgfx/c99/bgfx.h"
REQUIRE_REAL_LIBRARIES="${REQUIRE_REAL_LIBRARIES:-0}"

if [[ ! -f "${MINIAUDIO_HEADER}" || ! -f "${SDL3_HEADER}" || ! -f "${CIMGUI_HEADER}" || ! -f "${CIMGUIZMO_HEADER}" || ! -f "${BGFX_HEADER}" ]]; then
  if [[ "${REQUIRE_REAL_LIBRARIES}" == "1" ]]; then
    echo "Required real-library headers were not found under: ${INNOENGINE_ROOT}"
    exit 1
  fi
  echo "[real-libraries] InnoEngine headers not found; skipping optional local matrix."
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
BX_INCLUDE_JSON="${INNOENGINE_ROOT}/extern/bx/include"
if command -v cygpath > /dev/null 2>&1; then BX_INCLUDE_JSON="$(cygpath -m "${BX_INCLUDE_JSON}")"; fi

"${DOTNET_CMD}" build "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release -m:1 /nodeReuse:false
"${DOTNET_CMD}" build "${ROOT_DIR}/scripts/BGCS.ApiSnapshot/BGCS.ApiSnapshot.csproj" --configuration Release -m:1 /nodeReuse:false

cat > "${ARTIFACTS_DIR}/miniaudio/bindgen.json" <<EOF
{
  "Preset": "host-c,c-library",
  "Namespace": "BGCS.RealLibraries.MiniAudio",
  "ApiName": "MiniAudio",
  "LibName": "miniaudio",
  "EntryFiles": ["${MINIAUDIO_HEADER_JSON}"],
  "AllowedHeaders": [],
  "IncludeTransitivelyReferencedHeaders": true,
  "IncludeFolders": ["${MINIAUDIO_INCLUDE_JSON}"],
  "IgnoredFunctions": ["ma_log_postf"],
  "OutputPath": "Generated"
}
EOF

start_seconds="$(date +%s)"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/miniaudio/bindgen.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
if (( elapsed_seconds > 60 )); then
  echo "[real-libraries] miniaudio generation exceeded 60 seconds: ${elapsed_seconds}s"
  exit 1
fi

cat > "${ARTIFACTS_DIR}/miniaudio/consumer/MiniAudio.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Generated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF

"${DOTNET_CMD}" build "${ARTIFACTS_DIR}/miniaudio/consumer/MiniAudio.Generated.csproj" --configuration Release -m:1 /nodeReuse:false
printf '[real-libraries] miniaudio passed in %ss.\n' "${elapsed_seconds}"

mkdir -p "${ARTIFACTS_DIR}/sdl3/consumer"
cat > "${ARTIFACTS_DIR}/sdl3/bindgen.json" <<EOF
{
  "Preset": "host-c,c-library,opaque-callbacks",
  "Namespace": "BGCS.RealLibraries.SDL3",
  "ApiName": "SDL3",
  "LibName": "SDL3",
  "EntryFiles": ["${SDL3_HEADER_JSON}"],
  "AllowedHeaders": [],
  "IncludeTransitivelyReferencedHeaders": true,
  "IncludeFolders": ["${SDL3_INCLUDE_JSON}"],
  "IgnoredFunctions": [
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
  "OutputPath": "Generated"
}
EOF

start_seconds="$(date +%s)"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/sdl3/bindgen.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
if (( elapsed_seconds > 45 )); then
  echo "[real-libraries] SDL3 generation exceeded 45 seconds: ${elapsed_seconds}s"
  exit 1
fi

cat > "${ARTIFACTS_DIR}/sdl3/consumer/SDL3.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Generated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF

"${DOTNET_CMD}" build "${ARTIFACTS_DIR}/sdl3/consumer/SDL3.Generated.csproj" --configuration Release -m:1 /nodeReuse:false
printf '[real-libraries] SDL3 passed in %ss.\n' "${elapsed_seconds}"

mkdir -p "${ARTIFACTS_DIR}/cimgui/consumer"
cat > "${ARTIFACTS_DIR}/cimgui/bindgen.json" <<EOF
{
  "Preset": "host-c,c-library,opaque-callbacks",
  "Namespace": "BGCS.RealLibraries.CImGui",
  "ApiName": "CImGui",
  "LibName": "cimgui",
  "EntryFiles": ["${CIMGUI_HEADER_JSON}"],
  "AllowedHeaders": [],
  "IncludeTransitivelyReferencedHeaders": true,
  "IncludeFolders": ["${CIMGUI_INCLUDE_JSON}"],
  "Defines": ["CIMGUI_DEFINE_ENUMS_AND_STRUCTS"],
  "IgnoredFunctions": [
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
  "OutputPath": "Generated"
}
EOF

start_seconds="$(date +%s)"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/cimgui/bindgen.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
if (( elapsed_seconds > 30 )); then
  echo "[real-libraries] cimgui generation exceeded 30 seconds: ${elapsed_seconds}s"
  exit 1
fi

cat > "${ARTIFACTS_DIR}/cimgui/consumer/CImGui.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Generated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF

"${DOTNET_CMD}" build "${ARTIFACTS_DIR}/cimgui/consumer/CImGui.Generated.csproj" --configuration Release -m:1 /nodeReuse:false
printf '[real-libraries] cimgui passed in %ss.\n' "${elapsed_seconds}"

mkdir -p "${ARTIFACTS_DIR}/cimguizmo/consumer"
cat > "${ARTIFACTS_DIR}/cimguizmo/bindgen.json" <<EOF
{
  "Preset": "host-c,c-library,opaque-callbacks",
  "Namespace": "BGCS.RealLibraries.CImGuizmo",
  "ApiName": "CImGuizmo",
  "LibName": "cimguizmo",
  "EntryFiles": ["${CIMGUIZMO_HEADER_JSON}"],
  "AllowedHeaders": [],
  "IncludeTransitivelyReferencedHeaders": true,
  "IncludeFolders": ["${CIMGUIZMO_INCLUDE_JSON}", "${CIMGUI_INCLUDE_JSON}"],
  "Defines": ["CIMGUI_DEFINE_ENUMS_AND_STRUCTS"],
  "IgnoredFunctions": [
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
  "OutputPath": "Generated"
}
EOF

start_seconds="$(date +%s)"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/cimguizmo/bindgen.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
if (( elapsed_seconds > 15 )); then
  echo "[real-libraries] cimguizmo generation exceeded 15 seconds: ${elapsed_seconds}s"
  exit 1
fi

cat > "${ARTIFACTS_DIR}/cimguizmo/consumer/CImGuizmo.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Generated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF

"${DOTNET_CMD}" build "${ARTIFACTS_DIR}/cimguizmo/consumer/CImGuizmo.Generated.csproj" --configuration Release -m:1 /nodeReuse:false
printf '[real-libraries] cimguizmo passed in %ss.\n' "${elapsed_seconds}"

mkdir -p "${ARTIFACTS_DIR}/bgfx/consumer"
cat > "${ARTIFACTS_DIR}/bgfx/bindgen.json" <<EOF
{
  "Preset": "host-c,c-library,opaque-callbacks",
  "Namespace": "BGCS.RealLibraries.Bgfx",
  "ApiName": "Bgfx",
  "LibName": "bgfx",
  "EntryFiles": ["${BGFX_HEADER_JSON}"],
  "AllowedHeaders": [],
  "IncludeTransitivelyReferencedHeaders": true,
  "IncludeFolders": ["${BGFX_INCLUDE_JSON}", "${BX_INCLUDE_JSON}"],
  "IgnoredFunctions": ["bgfx_dbg_text_printf"],
  "OutputPath": "Generated"
}
EOF
start_seconds="$(date +%s)"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/bgfx/bindgen.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
if (( elapsed_seconds > 30 )); then
  echo "[real-libraries] bgfx generation exceeded 30 seconds: ${elapsed_seconds}s"
  exit 1
fi
cat > "${ARTIFACTS_DIR}/bgfx/consumer/Bgfx.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../Generated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF
"${DOTNET_CMD}" build "${ARTIFACTS_DIR}/bgfx/consumer/Bgfx.Generated.csproj" --configuration Release -m:1 /nodeReuse:false
printf '[real-libraries] bgfx passed in %ss.\n' "${elapsed_seconds}"

verify_ir_backend() {
  local library_name="$1"
  local project_name="$2"
  local library_dir="${ARTIFACTS_DIR}/${library_name}"
  local consumer_dir="${library_dir}/ir-consumer"
  mkdir -p "${consumer_dir}"

  cat > "${library_dir}/bindgen.ir.json" <<EOF
{
  "BaseConfig": { "Url": "file://bindgen.json" },
  "CSharpEmissionBackend": "IntermediateRepresentation",
  "SingleFileOutputName": "Bindings.cs",
  "OutputPath": "IRGenerated"
}
EOF

  local start_seconds
  local elapsed_seconds
  start_seconds="$(date +%s)"
  "${DOTNET_CMD}" run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- \
    "${library_dir}/bindgen.ir.json"
  elapsed_seconds="$(( $(date +%s) - start_seconds ))"
  if (( elapsed_seconds > 120 )); then
    echo "[real-libraries] ${library_name} IR generation exceeded 120 seconds: ${elapsed_seconds}s"
    exit 1
  fi

  cat > "${consumer_dir}/${project_name}.IR.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="../IRGenerated/Bindings.cs" Link="Bindings.cs" />
    <ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" />
  </ItemGroup>
</Project>
EOF

  "${DOTNET_CMD}" build "${consumer_dir}/${project_name}.IR.Generated.csproj" --configuration Release -m:1 /nodeReuse:false
  printf '[real-libraries] %s IR-native ABI generation and warning-free compilation passed in %ss.\n' \
    "${library_name}" "${elapsed_seconds}"
}

verify_ir_backend "miniaudio" "MiniAudio"
verify_ir_backend "sdl3" "SDL3"
verify_ir_backend "cimgui" "CImGui"
verify_ir_backend "cimguizmo" "CImGuizmo"
verify_ir_backend "bgfx" "Bgfx"

"${DOTNET_CMD}" run --project "${ROOT_DIR}/scripts/BGCS.ApiSnapshot/BGCS.ApiSnapshot.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/miniaudio/consumer/bin/Release/net9.0/MiniAudio.Generated.dll" "${ARTIFACTS_DIR}/miniaudio/public-api.txt"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/scripts/BGCS.ApiSnapshot/BGCS.ApiSnapshot.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/sdl3/consumer/bin/Release/net9.0/SDL3.Generated.dll" "${ARTIFACTS_DIR}/sdl3/public-api.txt"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/scripts/BGCS.ApiSnapshot/BGCS.ApiSnapshot.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/cimgui/consumer/bin/Release/net9.0/CImGui.Generated.dll" "${ARTIFACTS_DIR}/cimgui/public-api.txt"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/scripts/BGCS.ApiSnapshot/BGCS.ApiSnapshot.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/cimguizmo/consumer/bin/Release/net9.0/CImGuizmo.Generated.dll" "${ARTIFACTS_DIR}/cimguizmo/public-api.txt"
"${DOTNET_CMD}" run --project "${ROOT_DIR}/scripts/BGCS.ApiSnapshot/BGCS.ApiSnapshot.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/bgfx/consumer/bin/Release/net9.0/Bgfx.Generated.dll" "${ARTIFACTS_DIR}/bgfx/public-api.txt"

pushd "${ROOT_DIR}" > /dev/null
SOURCE_SNAPSHOT_MANIFEST="$(resolve_snapshot_manifest "tests/real-libraries/api-snapshots")"
PUBLIC_API_SNAPSHOT_MANIFEST="$(resolve_snapshot_manifest "tests/real-libraries/public-api-snapshots")"
verify_sha256_manifest "${SOURCE_SNAPSHOT_MANIFEST}"
verify_sha256_manifest "${PUBLIC_API_SNAPSHOT_MANIFEST}"
popd > /dev/null
echo "[real-libraries] deterministic source and public API snapshots passed for $(detect_snapshot_platform)."
