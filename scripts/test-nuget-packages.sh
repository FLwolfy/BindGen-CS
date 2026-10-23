#!/usr/bin/env bash
set -euo pipefail

task_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${task_root}/scripts/lib/common.sh"

# A developer shell can select bin/x64 output while the closed package collector
# deliberately reads the projects' default output paths. Use the declared
# project defaults, retaining VC/Windows SDK discovery for native providers.
unset Platform PLATFORM platform

dotnet_command="$(resolve_dotnet_host)"
python_command="${PYTHON:-python3}"
configuration="${CONFIGURATION:-Release}"
version="${PACKAGE_TEST_VERSION:-}"
if [[ -z "${version}" ]]; then
  version="$("${python_command}" "${task_root}/scripts/acceptance_evidence.py" package-version "${task_root}" | tr -d '\r')"
fi
package_directory="${BGCS_PACKAGE_TEST_ARTIFACTS_ROOT:-${task_root}/artifacts}/nuget"
pack_arguments=(--dotnet "${dotnet_command}" --configuration "${configuration}" --version "${version}" --output "${package_directory}" --verify-repeat-content)
verify_arguments=(--dotnet "${dotnet_command}" --version "${version}" --packages "${package_directory}" --output "${task_root}/artifacts/verification/packages")
if [[ -n "${BGCS_OSX_X64_PACKAGE_RUNTIME_DIR:-}" ]]; then
  pack_arguments+=(--parser-osx-x64 "${BGCS_OSX_X64_PACKAGE_RUNTIME_DIR}")
else
  host_rid="$("${dotnet_command}" msbuild "${task_root}/src/BGCS/BGCS.csproj" -getProperty:NETCoreSdkRuntimeIdentifier -nologo | tr -d '[:space:]')"
  pack_arguments+=(--host-only "${host_rid}" --include-host-tool)
  verify_arguments+=(--sdk-only --host-tool)
  printf '[packages] No accepted Intel macOS parser assets supplied; this run verifies the local SDK and installed host tool. Full six-RID product certification remains required.\n'
fi
"${python_command}" "${task_root}/eng/pack.py" "${pack_arguments[@]}"
"${python_command}" "${task_root}/eng/verify-packages.py" "${verify_arguments[@]}"
printf '[packages] Installed products completed their declared consumption checks.\n'
