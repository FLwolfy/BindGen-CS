#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${ROOT_DIR}/scripts/lib/common.sh"
DOTNET_CMD="$(resolve_dotnet_host)"
DOTNET_ROOT_DIR="$(cd "$(dirname "${DOTNET_CMD}")" && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT_DIR}"
CONFIGURATION="${CONFIGURATION:-Release}"
VERSION="${PACKAGE_TEST_VERSION:-0.0.0-local}"
PACKAGE_DIR="${ROOT_DIR}/artifacts/nuget"
SECOND_PACKAGE_DIR="${ROOT_DIR}/artifacts/nuget-repeat"
CONSUMER_DIR="${ROOT_DIR}/artifacts/nuget-consumer"
CONSUMER_PACKAGE_CACHE="${ROOT_DIR}/artifacts/nuget-consumer-packages"
TOOL_DIR="${ROOT_DIR}/artifacts/nuget-tool"
TOOL_SMOKE_DIR="${ROOT_DIR}/artifacts/nuget-tool-smoke"
GLOBAL_PACKAGE_CACHE="${NUGET_PACKAGES:-}"

if [[ -z "${GLOBAL_PACKAGE_CACHE}" ]]; then
  global_packages_output="$("${DOTNET_CMD}" nuget locals global-packages --list)"
  GLOBAL_PACKAGE_CACHE="${global_packages_output#*: }"
fi
if [[ ! -d "${GLOBAL_PACKAGE_CACHE}" ]]; then
  printf 'The restored NuGet global-packages fallback does not exist: %s\n' "${GLOBAL_PACKAGE_CACHE}" >&2
  exit 1
fi

rm -rf "${PACKAGE_DIR}" "${SECOND_PACKAGE_DIR}" "${CONSUMER_DIR}" "${CONSUMER_PACKAGE_CACHE}" "${TOOL_DIR}" "${TOOL_SMOKE_DIR}"
mkdir -p "${PACKAGE_DIR}" "${SECOND_PACKAGE_DIR}" "${CONSUMER_DIR}" "${CONSUMER_PACKAGE_CACHE}" "${TOOL_DIR}" "${TOOL_SMOKE_DIR}"

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
  "${DOTNET_CMD}" pack "${ROOT_DIR}/${project}" --configuration "${CONFIGURATION}" --no-restore --output "${PACKAGE_DIR}" -p:ContinuousIntegrationBuild=true -p:UseSharedCompilation=false -p:Version="${VERSION}"
  "${DOTNET_CMD}" pack "${ROOT_DIR}/${project}" --configuration "${CONFIGURATION}" --no-restore --output "${SECOND_PACKAGE_DIR}" -p:ContinuousIntegrationBuild=true -p:UseSharedCompilation=false -p:Version="${VERSION}"
done

if ! command -v unzip > /dev/null 2>&1; then
  printf 'unzip is required for deterministic package-content verification.\n' >&2
  exit 1
fi

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
using var context = new NativeLibraryContext(IntPtr.Zero);
Console.WriteLine($"{config.ImportType}:{bridgeConfig.NamePrefix}:{parserOptions.ParserKind}:{context.IsExtensionSupported(string.Empty)}");
EOF

"${DOTNET_CMD}" restore "${CONSUMER_DIR}/PackageConsumer.csproj" \
  --packages "${CONSUMER_PACKAGE_CACHE}" \
  --source "${PACKAGE_DIR}" \
  --ignore-failed-sources \
  -p:RestoreAdditionalProjectFallbackFolders="${GLOBAL_PACKAGE_CACHE}"
"${DOTNET_CMD}" run --project "${CONSUMER_DIR}/PackageConsumer.csproj" --configuration "${CONFIGURATION}" --no-restore
"${DOTNET_CMD}" tool install BindGen-CS --version "${VERSION}" --tool-path "${TOOL_DIR}" --add-source "${PACKAGE_DIR}" --ignore-failed-sources
"${TOOL_DIR}/bindgen-cs" --help
cat > "${TOOL_SMOKE_DIR}/native.h" <<'EOF'
typedef struct NativePoint { int x; int y; } NativePoint;
int native_add(int left, int right);
EOF
cat > "${TOOL_SMOKE_DIR}/sample.hpp" <<'EOF'
class Demo { public: int Add(int value); };
EOF
cat > "${TOOL_SMOKE_DIR}/bridge.json" <<'EOF'
{
  "EntryFiles": ["sample.hpp"],
  "AllowedHeaders": ["sample.hpp"],
  "OutputPath": "GeneratedBridge"
}
EOF
pushd "${TOOL_SMOKE_DIR}" > /dev/null
"${TOOL_DIR}/bindgen-cs" init
"${TOOL_DIR}/bindgen-cs" doctor
"${TOOL_DIR}/bindgen-cs" validate
"${TOOL_DIR}/bindgen-cs" inspect
"${TOOL_DIR}/bindgen-cs" schema bindgen.schema.json
"${TOOL_DIR}/bindgen-cs" generate
"${TOOL_DIR}/bindgen-cs" diff
"${TOOL_DIR}/bindgen-cs" build
"${TOOL_DIR}/bindgen-cs" bridge bridge.json
if [[ ! -f "Generated/Bindings.cs" ]]; then
  echo "bindgen-cs did not produce Generated/Bindings.cs"
  exit 1
fi
if [[ ! -f "GeneratedBridge/include/Classes.h" ]]; then
  echo "bindgen-cs bridge did not produce GeneratedBridge/include/Classes.h"
  exit 1
fi
popd > /dev/null
