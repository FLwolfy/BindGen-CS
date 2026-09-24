#!/usr/bin/env bash

resolve_dotnet_host() {
  local candidate=""
  for candidate in \
    "${DOTNET_HOST_PATH:-}" \
    "${DOTNET_ROOT:-}/dotnet" \
    "${HOME:-}/.dotnet/dotnet" \
    "/usr/local/share/dotnet/dotnet" \
    "/usr/share/dotnet/dotnet"; do
    if [[ -n "${candidate}" && -x "${candidate}" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi
  done
  if command -v dotnet > /dev/null 2>&1; then
    command -v dotnet
    return 0
  fi
  printf 'Unable to find dotnet. Set DOTNET_HOST_PATH or add dotnet to PATH.\n' >&2
  return 1
}

resolve_cxx_host() {
  local candidate=""
  for candidate in "${BGCS_CPP2C_CXX:-}" "${CXX:-}" clang++ g++ c++; do
    if [[ -z "${candidate}" ]]; then
      continue
    fi
    if [[ -x "${candidate}" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi
    if command -v "${candidate}" > /dev/null 2>&1; then
      command -v "${candidate}"
      return 0
    fi
  done
  if [[ "${OS:-}" == "Windows_NT" && -x "C:/Program Files/LLVM/bin/clang++.exe" ]]; then
    printf '%s\n' "C:/Program Files/LLVM/bin/clang++.exe"
    return 0
  fi
  printf 'Unable to find a C++ compiler. Set BGCS_CPP2C_CXX or CXX.\n' >&2
  return 1
}

snapshot_sha256() {
  local path="$1" checksum
  if [[ ! -f "${path}" ]]; then
    printf 'Snapshot input does not exist: %s\n' "${path}" >&2
    return 1
  fi
  # The ABI reference is provenance, not generated C# API or implementation.
  # Compare all other bytes against the reviewed, pre-annotation baselines.
  if [[ "${path}" == *.cs ]]; then
    # Git Bash sed defaults to text mode on Windows, which rewrites CRLF bytes
    # while filtering the annotation. GNU sed -b keeps the snapshot byte-exact.
    local -a sed_command=(sed)
    if sed -b -e '' /dev/null > /dev/null 2>&1; then
      sed_command=(sed -b)
    fi
    if command -v sha256sum > /dev/null 2>&1; then
      checksum="$("${sed_command[@]}" '1,12{/^\/\/ *ABI reference target: /d;}' "${path}" | sha256sum --binary)" || return 1
    elif command -v shasum > /dev/null 2>&1; then
      checksum="$("${sed_command[@]}" '1,12{/^\/\/ *ABI reference target: /d;}' "${path}" | shasum -a 256 --binary)" || return 1
    else
      printf 'Unable to hash %s: sha256sum or shasum is required.\n' "${path}" >&2
      return 1
    fi
  elif command -v sha256sum > /dev/null 2>&1; then
    checksum="$(sha256sum --binary "${path}")" || return 1
  elif command -v shasum > /dev/null 2>&1; then
    checksum="$(shasum -a 256 --binary "${path}")" || return 1
  else
    printf 'Unable to hash %s: sha256sum or shasum is required.\n' "${path}" >&2
    return 1
  fi
  printf '%s\n' "${checksum%% *}"
}

verify_sha256_manifest() {
  local manifest="$1" record expected path actual failed=0
  while IFS= read -r record || [[ -n "${record}" ]]; do
    record="${record%$'\r'}"
    [[ -z "${record}" ]] && continue
    if [[ ! "${record:0:64}" =~ ^[[:xdigit:]]{64}$ || "${record:64:2}" != ' *' ]]; then
      printf 'Malformed snapshot manifest entry: %s\n' "${record}" >&2
      return 1
    fi
    expected="${record:0:64}"
    path="${record:66}"
    actual="$(snapshot_sha256 "${path}")" || return 1
    if [[ "${actual}" == "${expected}" ]]; then
      printf '%s: OK\n' "${path}"
    else
      printf '%s: FAILED\n' "${path}" >&2
      failed=1
    fi
  done < "${manifest}"
  return "${failed}"
}

detect_snapshot_platform() {
  if [[ -n "${BGCS_SNAPSHOT_PLATFORM:-}" ]]; then
    printf '%s\n' "${BGCS_SNAPSHOT_PLATFORM}"
    return 0
  fi

  local kernel architecture platform
  kernel="$(uname -s)"
  architecture="$(uname -m)"
  case "${kernel}" in
    Darwin) platform="macos" ;;
    Linux) platform="linux" ;;
    MINGW*|MSYS*|CYGWIN*) platform="windows" ;;
    *)
      printf 'Unsupported snapshot platform kernel: %s\n' "${kernel}" >&2
      return 1
      ;;
  esac
  case "${architecture}" in
    x86_64|amd64|AMD64) architecture="x64" ;;
    arm64|aarch64|ARM64) architecture="arm64" ;;
    *)
      printf 'Unsupported snapshot architecture: %s\n' "${architecture}" >&2
      return 1
      ;;
  esac
  printf '%s-%s\n' "${platform}" "${architecture}"
}

# A new or changed host must supply a reviewed baseline before it can pass
# acceptance. Preserve the exact candidate so a failed run gathers evidence
# without silently blessing an API change.
verify_or_capture_snapshot_manifest() {
  local manifest_prefix="$1"
  shift
  if [[ "$#" -eq 0 ]]; then
    printf 'Snapshot verification requires at least one generated file.\n' >&2
    return 1
  fi
  local platform manifest candidate_dir candidate
  platform="$(detect_snapshot_platform)" || return 1
  manifest="${manifest_prefix}.${platform}.sha256"
  local unreviewed_reason="No reviewed snapshot manifest"
  if [[ -f "${manifest}" ]]; then
    if verify_sha256_manifest "${manifest}"; then
      return 0
    fi
    unreviewed_reason="Reviewed snapshot mismatch"
  fi
  candidate_dir="${ROOT_DIR}/artifacts/acceptance/candidate-snapshots"
  mkdir -p "${candidate_dir}" || return 1
  candidate="${candidate_dir}/$(basename "${manifest}")"
  : > "${candidate}"
  local path checksum
  for path in "$@"; do
    checksum="$(snapshot_sha256 "${path}")" || { rm -f "${candidate}"; return 1; }
    printf '%s *%s\n' "${checksum}" "${path}" >> "${candidate}"
  done
  printf '%s for %s. Candidate: %s\n' "${unreviewed_reason}" "${platform}" "${candidate}" >&2
  return 3
}
