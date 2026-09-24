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

verify_sha256_manifest() {
  local manifest="$1"
  if command -v sha256sum > /dev/null 2>&1; then
    sha256sum --check "${manifest}"
    return
  fi
  if command -v shasum > /dev/null 2>&1; then
    shasum -a 256 --check "${manifest}"
    return
  fi
  printf 'Unable to verify %s: sha256sum or shasum is required.\n' "${manifest}" >&2
  return 1
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

# A new host must supply a reviewed baseline before it can pass acceptance.
# Preserve an exact candidate under acceptance artifacts so one failed run
# gathers the hashes needed for review rather than silently blessing itself.
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
  if [[ -f "${manifest}" ]]; then
    verify_sha256_manifest "${manifest}"
    return
  fi
  candidate_dir="${ROOT_DIR}/artifacts/acceptance/candidate-snapshots"
  mkdir -p "${candidate_dir}" || return 1
  candidate="${candidate_dir}/$(basename "${manifest}")"
  if command -v sha256sum > /dev/null 2>&1; then
    if ! sha256sum --binary "$@" > "${candidate}"; then
      rm -f "${candidate}"
      return 1
    fi
  elif command -v shasum > /dev/null 2>&1; then
    if ! shasum -a 256 --binary "$@" > "${candidate}"; then
      rm -f "${candidate}"
      return 1
    fi
  else
    printf 'Unable to create snapshot candidate: sha256sum or shasum is required.\n' >&2
    return 1
  fi
  printf 'No reviewed snapshot manifest for %s. Candidate: %s\n' "${platform}" "${candidate}" >&2
  return 3
}
