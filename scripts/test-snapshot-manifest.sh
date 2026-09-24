#!/usr/bin/env bash
set -euo pipefail

SOURCE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "${SOURCE_ROOT}/scripts/lib/common.sh"

TEST_ROOT="$(mktemp -d)"
trap 'rm -rf -- "${TEST_ROOT}"' EXIT
cd "${TEST_ROOT}"
ROOT_DIR="${TEST_ROOT}"
BGCS_SNAPSHOT_PLATFORM="fixture-x64"
mkdir -p tests/real-libraries
printf 'first version\n' > output.txt

if command -v sha256sum > /dev/null 2>&1; then
  manifest_line="$(sha256sum --binary output.txt)"
else
  manifest_line="$(shasum -a 256 --binary output.txt)"
fi
printf '%s\r\n' "${manifest_line}" > tests/real-libraries/api-snapshots.fixture-x64.sha256

# CRLF manifests from a Windows checkout must verify against exact file bytes.
verify_or_capture_snapshot_manifest tests/real-libraries/api-snapshots output.txt

# A changed generated file must remain a failure and yield a review candidate.
printf 'second version\n' > output.txt
status=0
verify_or_capture_snapshot_manifest tests/real-libraries/api-snapshots output.txt || status=$?
[[ "${status}" == "3" ]]
verify_sha256_manifest artifacts/acceptance/candidate-snapshots/api-snapshots.fixture-x64.sha256

# A platform without a baseline follows the same fail-closed candidate path.
status=0
verify_or_capture_snapshot_manifest tests/real-libraries/new-api-snapshots output.txt || status=$?
[[ "${status}" == "3" ]]
verify_sha256_manifest artifacts/acceptance/candidate-snapshots/new-api-snapshots.fixture-x64.sha256

printf '[snapshots] CRLF verification and fail-closed candidate capture passed.\n'
