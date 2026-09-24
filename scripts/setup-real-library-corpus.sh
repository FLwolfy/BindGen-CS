#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CORPUS_DIR="${BGCS_REAL_LIBRARY_ROOT:-${ROOT_DIR}/artifacts/real-library-corpus}"

clone_pinned() {
  local name="$1" repository="$2" revision="$3" sparse_directory="${4:-}"
  local destination="${CORPUS_DIR}/${name}"
  if [[ -e "${destination}" ]]; then
    if [[ "$(git -C "${destination}" rev-parse HEAD 2>/dev/null)" != "${revision}" ]]; then
      printf 'Corpus directory is present but is not the pinned revision: %s\n' "${destination}" >&2
      exit 1
    fi
    return
  fi
  local staging
  staging="$(mktemp -d "${CORPUS_DIR}/.${name}.XXXXXX")"
  if ! (
    git init --quiet "${staging}" || exit 1
    git -C "${staging}" remote add origin "https://github.com/${repository}.git" || exit 1
    git -C "${staging}" -c protocol.version=2 fetch --quiet --depth 1 --filter=blob:none origin "${revision}" || exit 1
    if [[ -n "${sparse_directory}" ]]; then
      git -C "${staging}" sparse-checkout set --cone "${sparse_directory}" || exit 1
    fi
    git -C "${staging}" -c advice.detachedHead=false checkout --quiet --detach FETCH_HEAD || exit 1
  ); then
    rm -rf -- "${staging}"
    printf 'Failed to fetch pinned corpus repository: %s\n' "${repository}" >&2
    exit 1
  fi
  if [[ "$(git -C "${staging}" rev-parse HEAD)" != "${revision}" ]]; then
    rm -rf -- "${staging}"
    printf 'Revision mismatch for %s.\n' "${repository}" >&2
    exit 1
  fi
  mv "${staging}" "${destination}"
}

mkdir -p "${CORPUS_DIR}"
clone_pinned miniaudio mackron/miniaudio 9634bedb5b5a2ca38c1ee7108a9358a4e233f14d extras/miniaudio_split
clone_pinned SDL libsdl-org/SDL 5f78ded3194a85ebdabc219f844811ba953b4450 include
clone_pinned cimgui cimgui/cimgui 715802490eabca2fc86cf25b41b83aa7c5d6060d
clone_pinned cimguizmo cimgui/cimguizmo 77e8ff47dc16a688edb06526b2f19c845b653bc7
clone_pinned bgfx bkaradzic/bgfx 20a637e3e6dd946031dd21e74c7138d2fab06abc include
clone_pinned bimg bkaradzic/bimg 6c13a0c8a8908243df475dfcab276554458dcd13 include
clone_pinned bx bkaradzic/bx 0d38df86151b73b7b471a9a7db31deb5f4ce02ca include

for header in \
  miniaudio/extras/miniaudio_split/miniaudio.h \
  SDL/include/SDL3/SDL.h \
  cimgui/cimgui.h \
  cimguizmo/cimguizmo.h \
  bgfx/include/bgfx/c99/bgfx.h \
  bimg/include/bimg/bimg.h \
  bx/include/bx/bx.h; do
  if [[ ! -f "${CORPUS_DIR}/${header}" ]]; then
    printf 'Pinned corpus is incomplete: %s\n' "${CORPUS_DIR}/${header}" >&2
    exit 1
  fi
done
printf 'Pinned, independent C/C++ corpus ready: %s\n' "${CORPUS_DIR}"
