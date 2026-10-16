#!/usr/bin/env bash
set -euo pipefail
if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "x86_64" ]]; then
  printf 'The frozen parser supplement applies to the Intel macOS launch host.\n' >&2
  exit 1
fi
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -z "$(brew list --versions llvm@20 2>/dev/null)" ]]; then brew install llvm@20; fi
expected="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["supplementalRuntime"]["llvm"]["version"])' "${root}/distribution/manifests/native-parser-assets.lock.json")"
actual="$("$(brew --prefix llvm@20)/bin/llvm-config" --version)"
if [[ "${actual}" != "${expected}" ]]; then printf 'The shard requires locked LLVM %s, found %s.\n' "${expected}" "${actual}" >&2; exit 1; fi
if [[ -z "${GITHUB_ENV:-}" || -z "${GITHUB_PATH:-}" ]]; then printf 'Use this setup only in the authenticated CI job environment.\n' >&2; exit 1; fi
runtime="${root}/artifacts/clang-runtime/osx-x64"
printf 'BGCS_CLANG_RUNTIME_DIR=%s\nBGCS_OSX_X64_PACKAGE_RUNTIME_DIR=%s\nDYLD_LIBRARY_PATH=%s\n' "${runtime}" "${runtime}" "${runtime}" >> "${GITHUB_ENV}"
printf '%s\n' "${root}/artifacts/clang-runtime/bin" >> "${GITHUB_PATH}"
