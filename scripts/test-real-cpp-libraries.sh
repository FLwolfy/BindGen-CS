#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
INNOENGINE_ROOT="${INNOENGINE_ROOT:-$(cd "${ROOT_DIR}/.." && pwd)/InnoEngine}"
ARTIFACTS_DIR="${ROOT_DIR}/artifacts/real-cpp-libraries"
BIMG_HEADER="${INNOENGINE_ROOT}/extern/bimg/include/bimg/bimg.h"
BIMG_INCLUDE="${INNOENGINE_ROOT}/extern/bimg/include"
BX_INCLUDE="${INNOENGINE_ROOT}/extern/bx/include"
if [[ ! -f "${BIMG_HEADER}" ]]; then
  echo "[real-cpp] bimg headers unavailable; skipping."
  exit 0
fi
rm -rf "${ARTIFACTS_DIR}"
mkdir -p "${ARTIFACTS_DIR}/bimg/consumer"
if command -v cygpath > /dev/null 2>&1; then
  BIMG_HEADER_JSON="$(cygpath -m "${BIMG_HEADER}")"
  BIMG_INCLUDE_JSON="$(cygpath -m "${BIMG_INCLUDE}")"
  BX_INCLUDE_JSON="$(cygpath -m "${BX_INCLUDE}")"
  ROOT_DIR_JSON="$(cygpath -m "${ROOT_DIR}")"
else
  BIMG_HEADER_JSON="${BIMG_HEADER}"
  BIMG_INCLUDE_JSON="${BIMG_INCLUDE}"
  BX_INCLUDE_JSON="${BX_INCLUDE}"
  ROOT_DIR_JSON="${ROOT_DIR}"
fi

dotnet build "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release
dotnet build "${ROOT_DIR}/scripts/BGCS.ApiSnapshot/BGCS.ApiSnapshot.csproj" --configuration Release
cat > "${ARTIFACTS_DIR}/bimg/bridge.json" <<EOF
{
  "EntryFiles": ["${BIMG_HEADER_JSON}"],
  "AllowedHeaders": ["${BIMG_HEADER_JSON}"],
  "IncludeFolders": ["${BIMG_INCLUDE_JSON}", "${BX_INCLUDE_JSON}"],
  "OutputPath": "Bridge",
  "GenerateCSharpBindings": true,
  "CSharpNamespace": "BGCS.RealLibraries.Bimg",
  "CSharpApiName": "Bimg",
  "NativeLibraryName": "bimg_bridge",
  "CSharpOutputPath": "GeneratedOneStep",
  "NamePrefix": "Bimg_",
  "ParseSystemIncludes": false,
  "ParseComments": false
}
EOF
start_seconds="$(date +%s)"
dotnet run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- bridge "${ARTIFACTS_DIR}/bimg/bridge.json"
elapsed_seconds="$(( $(date +%s) - start_seconds ))"
if (( elapsed_seconds > 15 )); then echo "[real-cpp] bimg bridge exceeded 15 seconds: ${elapsed_seconds}s"; exit 1; fi
"${BGCS_CPP2C_CXX:-C:/Program Files/LLVM/bin/clang++.exe}" -std=c++23 -fsyntax-only \
  -I "${ARTIFACTS_DIR}/bimg/Bridge/include" -I "${INNOENGINE_ROOT}/extern/bimg/include/bimg" \
  -I "${BIMG_INCLUDE}" -I "${BX_INCLUDE}" "${ARTIFACTS_DIR}/bimg/Bridge/src/Classes.cpp"
BRIDGE_HEADER_JSON="${ARTIFACTS_DIR}/bimg/Bridge/include/Classes.h"
if command -v cygpath > /dev/null 2>&1; then BRIDGE_HEADER_JSON="$(cygpath -m "${BRIDGE_HEADER_JSON}")"; fi
cat > "${ARTIFACTS_DIR}/bimg/bindgen.json" <<EOF
{
  "Preset": "windows-c",
  "Namespace": "BGCS.RealLibraries.Bimg",
  "ApiName": "Bimg",
  "LibName": "bimg_bridge",
  "AutoSquashTypedef": false,
  "ParseMacros": false,
  "ParseComments": false,
  "DelegatesAsVoidPointer": true,
  "EntryFiles": ["${BRIDGE_HEADER_JSON}"],
  "AllowedHeaders": [],
  "IncludeTransitivelyReferencedHeaders": true,
  "IncludeFolders": ["$(dirname "${BRIDGE_HEADER_JSON}")"],
  "OutputPath": "Generated",
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "OneFilePerType": false,
  "MergeGeneratedFilesToSingleFile": true
}
EOF
dotnet run --project "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" --configuration Release --no-build -- "${ARTIFACTS_DIR}/bimg/bindgen.json"
cat > "${ARTIFACTS_DIR}/bimg/consumer/Bimg.Generated.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework><AllowUnsafeBlocks>true</AllowUnsafeBlocks><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="..\\GeneratedOneStep\\Bindings.cs" Link="Bindings.cs" /><ProjectReference Include="${ROOT_DIR_JSON}/src/BGCS.Runtime/BGCS.Runtime.csproj" /></ItemGroup></Project>
EOF
dotnet build "${ARTIFACTS_DIR}/bimg/consumer/Bimg.Generated.csproj" --configuration Release
dotnet run --project "${ROOT_DIR}/scripts/BGCS.ApiSnapshot/BGCS.ApiSnapshot.csproj" --configuration Release --no-build -- \
  "${ARTIFACTS_DIR}/bimg/consumer/bin/Release/net9.0/Bimg.Generated.dll" "${ARTIFACTS_DIR}/bimg/public-api.txt"
pushd "${ROOT_DIR}" > /dev/null
sha256sum --check "tests/real-libraries/cpp-api-snapshots.sha256"
popd > /dev/null
echo "[real-cpp] bimg bridge, C# consumer, and API snapshots passed in ${elapsed_seconds}s."
