#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
DOTNET_ROOT_DIR="$(cd "$(dirname "${DOTNET_CMD}")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT_DIR}"
CONFIGURATION="${CONFIGURATION:-Release}"
# A unique local version prevents NuGet fallback folders from reusing an older
# package with the same test version. Release CI supplies PACKAGE_TEST_VERSION.
VERSION="${PACKAGE_TEST_VERSION:-0.0.0-local.run$(date -u +%s).$$}"
ARTIFACTS_ROOT="${BGCS_PACKAGE_TEST_ARTIFACTS_ROOT:-${ROOT_DIR}/artifacts}"
if [[ "${ARTIFACTS_ROOT}" != /* || "${ARTIFACTS_ROOT}" == "/" ||
      "${ARTIFACTS_ROOT}" == "${ROOT_DIR}" || "${ARTIFACTS_ROOT}" == "${HOME:-}" ]]; then
  printf 'BGCS_PACKAGE_TEST_ARTIFACTS_ROOT must be a dedicated absolute directory.\n' >&2
  exit 1
fi
PACKAGE_DIR="${ARTIFACTS_ROOT}/nuget"
SECOND_PACKAGE_DIR="${ARTIFACTS_ROOT}/nuget-repeat"
CONSUMER_DIR="${ARTIFACTS_ROOT}/nuget-consumer"
CONSUMER_PACKAGE_CACHE="${ARTIFACTS_ROOT}/nuget-consumer-packages"
TOOL_DIR="${ARTIFACTS_ROOT}/nuget-tool"
TOOL_SMOKE_DIR="${ARTIFACTS_ROOT}/nuget-tool-smoke"
NATIVE_PACKAGE_STAGE="${ARTIFACTS_ROOT}/native-package-layout"
NATIVE_PACKAGE_PROJECT="${ARTIFACTS_ROOT}/native-package-project"
NATIVE_PACKAGE_OUTPUT="${ARTIFACTS_ROOT}/native-package-output"
NATIVE_PACKAGE_CONSUMER="${ARTIFACTS_ROOT}/native-package-consumer"
GLOBAL_PACKAGE_CACHE="${NUGET_PACKAGES:-}"

if [[ -n "${BGCS_OSX_X64_PACKAGE_RUNTIME_DIR:-}" ]]; then
  for library in libclang.dylib libClangSharp.dylib; do
    if [[ ! -f "${BGCS_OSX_X64_PACKAGE_RUNTIME_DIR}/${library}" ]]; then
      printf 'macOS x64 package runtime is missing %s in %s.\n' \
        "${library}" "${BGCS_OSX_X64_PACKAGE_RUNTIME_DIR}" >&2
      exit 1
    fi
  done
fi

if [[ -z "${GLOBAL_PACKAGE_CACHE}" ]]; then
  global_packages_output="$("${DOTNET_CMD}" nuget locals global-packages --list)"
  GLOBAL_PACKAGE_CACHE="${global_packages_output#*: }"
fi
if [[ ! -d "${GLOBAL_PACKAGE_CACHE}" ]]; then
  printf 'The restored NuGet global-packages fallback does not exist: %s\n' "${GLOBAL_PACKAGE_CACHE}" >&2
  exit 1
fi

rm -rf "${PACKAGE_DIR}" "${SECOND_PACKAGE_DIR}" "${CONSUMER_DIR}" "${CONSUMER_PACKAGE_CACHE}" "${TOOL_DIR}" "${TOOL_SMOKE_DIR}" \
  "${NATIVE_PACKAGE_STAGE}" "${NATIVE_PACKAGE_PROJECT}" "${NATIVE_PACKAGE_OUTPUT}" "${NATIVE_PACKAGE_CONSUMER}"
mkdir -p "${PACKAGE_DIR}" "${SECOND_PACKAGE_DIR}" "${CONSUMER_DIR}" "${CONSUMER_PACKAGE_CACHE}" "${TOOL_DIR}" "${TOOL_SMOKE_DIR}" \
  "${NATIVE_PACKAGE_STAGE}" "${NATIVE_PACKAGE_PROJECT}" "${NATIVE_PACKAGE_OUTPUT}" "${NATIVE_PACKAGE_CONSUMER}"

projects=(
  "src/BGCS.Intermediate/BGCS.Intermediate.csproj"
  "src/BGCS.CppAst/BGCS.CppAst.csproj"
  "src/BGCS.Core/BGCS.Core.csproj"
  "src/BGCS.Language/BGCS.Language.csproj"
  "src/BGCS/BGCS.csproj"
  "src/BGCS.Cpp2C/BGCS.Cpp2C.csproj"
  "src/BGCS.Runtime/BGCS.Runtime.csproj"
  "src/BGCS.Tool/BGCS.Tool.csproj"
)

for project in "${projects[@]}"; do
  "${DOTNET_CMD}" pack "${ROOT_DIR}/${project}" --configuration "${CONFIGURATION}" --no-restore --output "${PACKAGE_DIR}" -m:1 -nodeReuse:false -p:ContinuousIntegrationBuild=true -p:UseSharedCompilation=false -p:Version="${VERSION}"
  "${DOTNET_CMD}" pack "${ROOT_DIR}/${project}" --configuration "${CONFIGURATION}" --no-restore --output "${SECOND_PACKAGE_DIR}" -m:1 -nodeReuse:false -p:ContinuousIntegrationBuild=true -p:UseSharedCompilation=false -p:Version="${VERSION}"
done

if ! command -v unzip > /dev/null 2>&1; then
  printf 'unzip is required for deterministic package-content verification.\n' >&2
  exit 1
fi

parser_package="${PACKAGE_DIR}/BGCS.CppAst.${VERSION}.nupkg"
parser_manifest="$(unzip -p "${parser_package}" BGCS.CppAst.nuspec)"
for rid in win-x64 win-arm64 linux-x64 linux-arm64 osx-arm64; do
  for native_package in libclang.runtime libClangSharp.runtime; do
    if ! grep -Fq "id=\"${native_package}.${rid}\"" <<< "${parser_manifest}"; then
      printf 'Parser package is missing the %s dependency for %s.\n' "${native_package}" "${rid}" >&2
      exit 1
    fi
  done
done
printf '[nuget] Parser package declares the complete published desktop native RID dependency closure.\n'

# nuget.org rejects any single package above 250 MB, so the tool ships as a small
# pointer package plus one package per runtime identifier.
TOOL_PACKAGE_RIDS=(win-x64 win-arm64 linux-x64 linux-arm64 osx-arm64)
NUGET_PACKAGE_SIZE_LIMIT=$((250 * 1024 * 1024))
tool_packages=("${PACKAGE_DIR}/BindGen-CS.${VERSION}.nupkg")
for rid in "${TOOL_PACKAGE_RIDS[@]}"; do
  tool_packages+=("${PACKAGE_DIR}/BindGen-CS.${rid}.${VERSION}.nupkg")
done
for tool_package in "${tool_packages[@]}"; do
  if [[ ! -f "${tool_package}" ]]; then
    printf 'Tool packaging did not produce %s.\n' "$(basename "${tool_package}")" >&2
    exit 1
  fi
  package_size="$(wc -c < "${tool_package}")"
  if (( package_size > NUGET_PACKAGE_SIZE_LIMIT )); then
    printf '%s is %s bytes and exceeds the %s byte nuget.org limit.\n' \
      "$(basename "${tool_package}")" "${package_size}" "${NUGET_PACKAGE_SIZE_LIMIT}" >&2
    exit 1
  fi
done
host_rid="$("${DOTNET_CMD}" msbuild "${ROOT_DIR}/src/BGCS.Tool/BGCS.Tool.csproj" \
  -getProperty:NETCoreSdkPortableRuntimeIdentifier -nologo | tr -d '[:space:]')"
if [[ ! " ${TOOL_PACKAGE_RIDS[*]} " == *" ${host_rid} "* ]]; then
  printf 'Host RID %s has no published tool package; the smoke test cannot run.\n' "${host_rid}" >&2
  exit 1
fi
printf '[nuget] Tool pointer package and %s RID packages are published within the size limit.\n' "${#TOOL_PACKAGE_RIDS[@]}"

shopt -s nullglob
first_packages=("${PACKAGE_DIR}"/*.nupkg)
second_packages=("${SECOND_PACKAGE_DIR}"/*.nupkg)
if (( ${#first_packages[@]} == 0 || ${#first_packages[@]} != ${#second_packages[@]} )); then
  printf 'The two pack runs produced different package sets.\n' >&2
  exit 1
fi
for first_package in "${first_packages[@]}"; do
  package_name="$(basename "${first_package}")"
  second_package="${SECOND_PACKAGE_DIR}/${package_name}"
  if [[ ! -f "${second_package}" ]]; then
    printf 'Second pack run did not produce %s.\n' "${package_name}" >&2
    exit 1
  fi
  first_extract="$(mktemp -d)"
  second_extract="$(mktemp -d)"
  unzip -qq "${first_package}" -d "${first_extract}"
  unzip -qq "${second_package}" -d "${second_extract}"
  find "${first_extract}" "${second_extract}" -type f -name '.signature.p7s' -delete
  first_core_properties="$(find "${first_extract}" -type f -name '*.psmdcp' -print)"
  second_core_properties="$(find "${second_extract}" -type f -name '*.psmdcp' -print)"
  if [[ -n "${first_core_properties}" && -n "${second_core_properties}" ]]; then
    mv "${first_core_properties}" "${first_extract}/__nuget_core_properties.psmdcp"
    mv "${second_core_properties}" "${second_extract}/__nuget_core_properties.psmdcp"
  elif [[ -n "${first_core_properties}" || -n "${second_core_properties}" ]]; then
    printf 'Only one pack run emitted NuGet core properties: %s\n' "${package_name}" >&2
    rm -rf "${first_extract}" "${second_extract}"
    exit 1
  fi
  rm -f "${first_extract}/_rels/.rels" "${second_extract}/_rels/.rels"
  if ! diff -qr "${first_extract}" "${second_extract}"; then
    printf 'Package content is not deterministic: %s\n' "${package_name}" >&2
    rm -rf "${first_extract}" "${second_extract}"
    exit 1
  fi
  rm -rf "${first_extract}" "${second_extract}"
done
printf '[nuget] Two clean pack runs produced equivalent package contents.\n'

cat > "${CONSUMER_DIR}/PackageConsumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BGCS" Version="${VERSION}" />
    <PackageReference Include="BGCS.Cpp2C" Version="${VERSION}" />
    <PackageReference Include="BGCS.Runtime" Version="${VERSION}" />
  </ItemGroup>
</Project>
EOF

cat > "${CONSUMER_DIR}/Program.cs" <<'EOF'
using BGCS;
using BGCS.Cpp2C;
using BGCS.CppAst.Parsing;
using BGCS.Runtime;

var config = new CsCodeGeneratorConfig();
var bridgeConfig = new Cpp2CGeneratorConfig();
var parserOptions = new CppParserOptions();
var parsed = CppParser.Parse("int bgcs_package_probe(void);", parserOptions);
if (parsed.HasErrors)
    throw new InvalidOperationException("The clean NuGet consumer could not load its native Clang runtime.");
using var context = new NativeLibraryContext(IntPtr.Zero);
Console.WriteLine($"{config.ImportType}:{bridgeConfig.NamePrefix}:{parserOptions.ParserKind}:{context.IsExtensionSupported(string.Empty)}");
EOF

"${DOTNET_CMD}" restore "${CONSUMER_DIR}/PackageConsumer.csproj" \
  --packages "${CONSUMER_PACKAGE_CACHE}" \
  --source "${PACKAGE_DIR}" \
  --ignore-failed-sources \
  -p:RestoreAdditionalProjectFallbackFolders="${GLOBAL_PACKAGE_CACHE}"
# A clean consumer must load the native assets selected from its package, not a
# build-host override (which may point at a different operating system's files).
env -u BGCS_CLANG_RUNTIME_DIR -u DYLD_LIBRARY_PATH \
  "${DOTNET_CMD}" run --project "${CONSUMER_DIR}/PackageConsumer.csproj" --configuration "${CONFIGURATION}" --no-restore
"${DOTNET_CMD}" tool install BindGen-CS --version "${VERSION}" --tool-path "${TOOL_DIR}" --add-source "${PACKAGE_DIR}" --ignore-failed-sources
# Windows installs bindgen-cs.exe or a .cmd launcher, unix-like hosts an
# extensionless shim; RID-specific tool packages can pick either form.
TOOL_EXE=""
for candidate in bindgen-cs bindgen-cs.exe bindgen-cs.cmd bindgen-cs.bat; do
  if [[ -f "${TOOL_DIR}/${candidate}" ]]; then
    TOOL_EXE="${TOOL_DIR}/${candidate}"
    break
  fi
done
if [[ -z "${TOOL_EXE}" ]]; then
  printf 'The installed tool shim was not found in %s:\n' "${TOOL_DIR}" >&2
  ls -A "${TOOL_DIR}" >&2
  exit 1
fi
"${TOOL_EXE}" --help
cat > "${TOOL_SMOKE_DIR}/native.h" <<'EOF'
typedef struct NativePoint { int x; int y; } NativePoint;
int native_add(int left, int right);
EOF
cat > "${TOOL_SMOKE_DIR}/sample.hpp" <<'EOF'
class Demo { public: int Add(int value) { return value + 17; } };
EOF
cat > "${TOOL_SMOKE_DIR}/bridge.json" <<'EOF'
{
  "EntryFiles": ["sample.hpp"],
  "AllowedHeaders": ["sample.hpp"],
  "OutputPath": "GeneratedBridge",
  "NativeLibraryName": "bgcs_package_probe"
}
EOF
pushd "${TOOL_SMOKE_DIR}" > /dev/null
"${TOOL_EXE}" init
"${TOOL_EXE}" doctor
"${TOOL_EXE}" validate
"${TOOL_EXE}" inspect
"${TOOL_EXE}" schema bindgen.schema.json
"${TOOL_EXE}" generate
"${TOOL_EXE}" diff
"${TOOL_EXE}" build
"${TOOL_EXE}" bridge bridge.json
if [[ ! -f "Generated/Bindings.cs" ]]; then
  echo "bindgen-cs did not produce Generated/Bindings.cs"
  exit 1
fi
if [[ ! -f "GeneratedBridge/include/Classes.h" ]]; then
  echo "bindgen-cs bridge did not produce GeneratedBridge/include/Classes.h"
  exit 1
fi
"${TOOL_EXE}" native-build GeneratedBridge/bridge.manifest.json \
  --package-root "${NATIVE_PACKAGE_STAGE}"
popd > /dev/null

cat > "${NATIVE_PACKAGE_PROJECT}/BGCS.NativeAsset.Probe.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <PackageId>BGCS.NativeAsset.Probe</PackageId>
    <Version>${VERSION}</Version>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
  </PropertyGroup>
  <ItemGroup>
    <!-- Keep paths relative to this project: Git Bash /d/... is not a Windows MSBuild path. -->
    <None Include="../native-package-layout/runtimes/**/*" Pack="true" PackagePath="runtimes/%(RecursiveDir)%(Filename)%(Extension)" />
    <None Include="../native-package-layout/bgcs.native-assets.json" Pack="true" PackagePath="bgcs.native-assets.json" />
  </ItemGroup>
</Project>
EOF
"${DOTNET_CMD}" restore "${NATIVE_PACKAGE_PROJECT}/BGCS.NativeAsset.Probe.csproj" --ignore-failed-sources
"${DOTNET_CMD}" pack "${NATIVE_PACKAGE_PROJECT}/BGCS.NativeAsset.Probe.csproj" \
  --configuration "${CONFIGURATION}" --no-restore --output "${NATIVE_PACKAGE_OUTPUT}" -m:1 -nodeReuse:false

cat > "${NATIVE_PACKAGE_CONSUMER}/NativePackageConsumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BGCS.NativeAsset.Probe" Version="${VERSION}" />
  </ItemGroup>
</Project>
EOF
cat > "${NATIVE_PACKAGE_CONSUMER}/Program.cs" <<'EOF'
using System.Runtime.InteropServices;

nint instance = NativeProbe.Create();
if (instance == 0)
    throw new InvalidOperationException("Native runtime asset returned a null instance.");
try
{
    int result = NativeProbe.Add(instance, 5);
    if (result != 22)
        throw new InvalidOperationException($"Native runtime asset returned {result}; expected 22.");
    Console.WriteLine($"multi-RID native package invocation passed: {result}");
}
finally
{
    NativeProbe.Destroy(instance);
}

internal static partial class NativeProbe
{
    [LibraryImport("bgcs_package_probe", EntryPoint = "DemoCreate")]
    internal static partial nint Create();

    [LibraryImport("bgcs_package_probe", EntryPoint = "Demo_Add")]
    internal static partial int Add(nint instance, int value);

    [LibraryImport("bgcs_package_probe", EntryPoint = "DemoDestroy")]
    internal static partial void Destroy(nint instance);
}
EOF
"${DOTNET_CMD}" restore "${NATIVE_PACKAGE_CONSUMER}/NativePackageConsumer.csproj" \
  --packages "${CONSUMER_PACKAGE_CACHE}" --source "${NATIVE_PACKAGE_OUTPUT}" --ignore-failed-sources
"${DOTNET_CMD}" run --project "${NATIVE_PACKAGE_CONSUMER}/NativePackageConsumer.csproj" \
  --configuration "${CONFIGURATION}" --no-restore
printf '[nuget] Current desktop RID native asset was selected and invoked from a clean package consumer.\n'
