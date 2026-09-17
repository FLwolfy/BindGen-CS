#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${CONFIGURATION:-Release}"
VERSION="${PACKAGE_TEST_VERSION:-0.0.0-local}"
PACKAGE_DIR="${ROOT_DIR}/artifacts/nuget"
CONSUMER_DIR="${ROOT_DIR}/artifacts/nuget-consumer"
TOOL_DIR="${ROOT_DIR}/artifacts/nuget-tool"
TOOL_SMOKE_DIR="${ROOT_DIR}/artifacts/nuget-tool-smoke"

rm -rf "${PACKAGE_DIR}" "${CONSUMER_DIR}" "${TOOL_DIR}" "${TOOL_SMOKE_DIR}"
mkdir -p "${PACKAGE_DIR}" "${CONSUMER_DIR}" "${TOOL_DIR}" "${TOOL_SMOKE_DIR}"

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
  dotnet pack "${ROOT_DIR}/${project}" --configuration "${CONFIGURATION}" --no-restore --output "${PACKAGE_DIR}" -p:ContinuousIntegrationBuild=true -p:Version="${VERSION}"
done

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

dotnet restore "${CONSUMER_DIR}/PackageConsumer.csproj" --source "${PACKAGE_DIR}"
dotnet run --project "${CONSUMER_DIR}/PackageConsumer.csproj" --configuration "${CONFIGURATION}" --no-restore
dotnet tool install BindGen-CS --version "${VERSION}" --tool-path "${TOOL_DIR}" --add-source "${PACKAGE_DIR}"
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
if [[ "${OS:-}" == "Windows_NT" ]]; then
  "${TOOL_DIR}/bindgen-cs" doctor
fi
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
