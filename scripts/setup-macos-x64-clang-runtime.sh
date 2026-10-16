#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "x86_64" ]]; then
  printf 'This bootstrap builds the missing ClangSharp 20 macOS x64 runtime on an Intel Mac.\n' >&2
  exit 1
fi

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUNTIME_DIR="${BGCS_CLANG_RUNTIME_DIR:-${ROOT_DIR}/artifacts/clang-runtime/osx-x64}"
SOURCE_DIR="${ROOT_DIR}/artifacts/clang-runtime/ClangSharp-source"
BUILD_DIR="${ROOT_DIR}/artifacts/clang-runtime/ClangSharp-build"
PARSER_LOCK="${ROOT_DIR}/distribution/manifests/native-parser-assets.lock.json"
EXPECTED_CLANGSHARP_COMMIT="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["supplementalRuntime"]["clangSharp"]["commit"])' "${PARSER_LOCK}")"
EXPECTED_LLVM_VERSION="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["supplementalRuntime"]["llvm"]["version"])' "${PARSER_LOCK}")"

if ! command -v brew >/dev/null 2>&1; then
  printf 'Homebrew is required to provide LLVM 20 on macOS x64.\n' >&2
  exit 1
fi
if [[ -z "$(brew list --versions llvm@20 2>/dev/null)" ]]; then
  brew install llvm@20
fi
LLVM_PREFIX="$(brew --prefix llvm@20)"
LLVM_REAL_PREFIX="$(cd "${LLVM_PREFIX}" && pwd -P)"
llvm_version="$("${LLVM_PREFIX}/bin/llvm-config" --version)"
if [[ "${llvm_version}" != "${EXPECTED_LLVM_VERSION}" ]]; then
  printf 'The parser recipe requires LLVM %s; the installed Homebrew keg reports %s.\n' "${EXPECTED_LLVM_VERSION}" "${llvm_version}" >&2
  exit 1
fi
if [[ ! -f "${LLVM_PREFIX}/lib/libclang.dylib" ]]; then
  printf 'LLVM 20 does not provide libclang.dylib at %s.\n' "${LLVM_PREFIX}" >&2
  exit 1
fi
COMPILER_BIN="${ROOT_DIR}/artifacts/clang-runtime/bin"
mkdir -p "${COMPILER_BIN}"
if printf '#include <expected>\nstd::expected<int, int> value(1);\n' |
  "${LLVM_PREFIX}/bin/clang++" -std=c++23 -x c++ -fsyntax-only -; then
  printf '#!/usr/bin/env bash\nexec "%s/bin/clang++" "$@"\n' "${LLVM_PREFIX}" > "${COMPILER_BIN}/clang++"
else
  if [[ ! -f "${LLVM_PREFIX}/include/c++/v1/expected" || ! -d "${LLVM_PREFIX}/lib/c++" ]] ||
    ! printf '#include <expected>\nstd::expected<int, int> value(1);\n' |
      "${LLVM_PREFIX}/bin/clang++" -std=c++23 -stdlib=libc++ -nostdinc++ \
        -isystem "${LLVM_PREFIX}/include/c++/v1" -x c++ -fsyntax-only -; then
    printf 'LLVM 20 cannot compile C++23 std::expected with the system or bundled libc++.\n' >&2
    exit 1
  fi
  printf '#!/usr/bin/env bash\nexec "%s/bin/clang++" -stdlib=libc++ -nostdinc++ -isystem "%s/include/c++/v1" -L"%s/lib/c++" -Wl,-rpath,"%s/lib/c++" "$@"\n' \
    "${LLVM_PREFIX}" "${LLVM_PREFIX}" "${LLVM_PREFIX}" "${LLVM_PREFIX}" > "${COMPILER_BIN}/clang++"
fi
chmod +x "${COMPILER_BIN}/clang++"
if [[ ! -d "${SOURCE_DIR}/.git" ]]; then
  git clone --depth 1 --branch v20.1.2.4 https://github.com/dotnet/ClangSharp.git "${SOURCE_DIR}"
fi
if [[ "$(git -C "${SOURCE_DIR}" rev-parse HEAD)" != "${EXPECTED_CLANGSHARP_COMMIT}" ]] ||
   [[ -n "$(git -C "${SOURCE_DIR}" status --porcelain --untracked-files=no)" ]]; then
  printf 'The ClangSharp checkout differs from the pinned clean source revision %s.\n' "${EXPECTED_CLANGSHARP_COMMIT}" >&2
  exit 1
fi
cmake -S "${SOURCE_DIR}" -B "${BUILD_DIR}" \
  -DCMAKE_BUILD_TYPE=Release \
  -DPATH_TO_LLVM="${LLVM_PREFIX}" \
  -DCMAKE_PREFIX_PATH="${LLVM_PREFIX}"
cmake --build "${BUILD_DIR}" --target ClangSharp --parallel 2

mkdir -p "${RUNTIME_DIR}"
if [[ -n "$(find "${RUNTIME_DIR}" -mindepth 1 -print -quit)" ]]; then
  printf 'Use a fresh empty owned runtime directory; existing parser bytes must not be reused as a new build.\n' >&2
  exit 1
fi
mkdir -p "${RUNTIME_DIR}/licenses"
mkdir -p "${RUNTIME_DIR}/build-inputs"
ORIGINS_PATH="${RUNTIME_DIR}/build-inputs/native-source-origins.tsv"
printf 'libclang.dylib\t%s\nlibClangSharp.dylib\t%s\n' \
  "$(realpath "${LLVM_PREFIX}/lib/libclang.dylib")" "$(realpath "${BUILD_DIR}/lib/libClangSharp.dylib")" > "${ORIGINS_PATH}"
cp -L "${LLVM_PREFIX}/lib/libclang.dylib" "${RUNTIME_DIR}/libclang.dylib"
cp -L "${BUILD_DIR}/lib/libClangSharp.dylib" "${RUNTIME_DIR}/libClangSharp.dylib"

record_dependency_license() {
  local source="$1"
  case "${source}" in
    "${LLVM_PREFIX}"/*|"${LLVM_REAL_PREFIX}"/*|"${BUILD_DIR}"/*) return ;;
  esac
  local source_directory
  source_directory="$(dirname "$(realpath "${source}")")"
  local cellar
  cellar="$(brew --cellar)"
  case "${source_directory}" in
    "${cellar}"/*) ;;
    *) printf 'Unlicensed external native dependency: %s\n' "${source}" >&2; exit 1 ;;
  esac
  local relative="${source_directory#${cellar}/}"
  local formula="${relative%%/*}"
  relative="${relative#*/}"
  local version="${relative%%/*}"
  local keg="${cellar}/${formula}/${version}"
  local found=0
  local candidate
  for candidate in "${keg}"/LICENSE* "${keg}"/COPYING* "${keg}"/share/doc/*/LICENSE*; do
    if [[ -f "${candidate}" ]]; then
      cp -L "${candidate}" "${RUNTIME_DIR}/licenses/${formula}-$(basename "${candidate}")"
      found=1
    fi
  done
  if [[ "${found}" != "1" ]]; then
    printf 'No redistributable license file found for %s (%s).\n' "${formula}" "${source}" >&2
    exit 1
  fi
}

# Make the native asset relocatable. Homebrew may link libclang through libLLVM
# and other dylibs; a consumer must not need the CI runner's Homebrew prefix.
runtime_files=("${RUNTIME_DIR}/libclang.dylib" "${RUNTIME_DIR}/libClangSharp.dylib")
runtime_index=0
while (( runtime_index < ${#runtime_files[@]} )); do
  runtime_file="${runtime_files[${runtime_index}]}"
  runtime_index=$((runtime_index + 1))
  while IFS= read -r dependency; do
    [[ -z "${dependency}" ]] && continue
    case "${dependency}" in
      /usr/lib/*|/System/Library/*) continue ;;
    esac
    dependency_name="$(basename "${dependency}")"
    if [[ "${dependency}" == "@loader_path/${dependency_name}" ]]; then
      continue
    fi
    dependency_source=""
    if [[ "${dependency}" == /* && -f "${dependency}" ]]; then
      dependency_source="${dependency}"
    elif [[ -f "${LLVM_PREFIX}/lib/${dependency_name}" ]]; then
      dependency_source="${LLVM_PREFIX}/lib/${dependency_name}"
    else
      for candidate in "$(brew --prefix)"/opt/*/lib/"${dependency_name}"; do
        if [[ -f "${candidate}" ]]; then
          dependency_source="${candidate}"
          break
        fi
      done
      if [[ -z "${dependency_source}" && -f "$(brew --prefix)/lib/${dependency_name}" ]]; then
        dependency_source="$(brew --prefix)/lib/${dependency_name}"
      fi
    fi
    if [[ -z "${dependency_source}" ]]; then
      printf 'Cannot resolve native dependency %s of %s.\n' "${dependency}" "${runtime_file}" >&2
      exit 1
    fi
    dependency_target="${RUNTIME_DIR}/${dependency_name}"
    if [[ ! -f "${dependency_target}" ]]; then
      record_dependency_license "${dependency_source}"
      cp -L "${dependency_source}" "${dependency_target}"
      printf '%s\t%s\n' "${dependency_name}" "$(realpath "${dependency_source}")" >> "${ORIGINS_PATH}"
      runtime_files+=("${dependency_target}")
    fi
    chmod u+w "${runtime_file}"
    install_name_tool -change "${dependency}" "@loader_path/${dependency_name}" "${runtime_file}"
  done < <(otool -L "${runtime_file}" | awk 'NR > 2 { print $1 }')
  install_name_tool -id "@loader_path/$(basename "${runtime_file}")" "${runtime_file}"
  codesign --force --sign - "${runtime_file}" >/dev/null
done

for runtime_file in "${runtime_files[@]}"; do
  if otool -L "${runtime_file}" | awk 'NR > 2 { print $1 }' |
    grep -Ev '^(@loader_path/|/usr/lib/|/System/Library/)' ; then
    printf 'Runtime is not relocatable: %s\n' "${runtime_file}" >&2
    exit 1
  fi
done

cp "${SOURCE_DIR}/LICENSE.md" "${RUNTIME_DIR}/licenses/ClangSharp-LICENSE.md"
cp "${SOURCE_DIR}/NOTICE.md" "${RUNTIME_DIR}/licenses/ClangSharp-NOTICE.md"
curl --fail --location --silent --show-error \
  "https://raw.githubusercontent.com/llvm/llvm-project/llvmorg-${llvm_version}/llvm/LICENSE.TXT" \
  --output "${RUNTIME_DIR}/licenses/LLVM-LICENSE.txt"

mkdir -p "${RUNTIME_DIR}/build-inputs"
brew info --json=v2 llvm@20 > "${RUNTIME_DIR}/build-inputs/homebrew-llvm.json"
python3 "${ROOT_DIR}/scripts/parser_runtime_provenance.py" create "${RUNTIME_DIR}" \
  --clang-source "${SOURCE_DIR}" --llvm-version "${llvm_version}" \
  --llvm-prefix "${LLVM_PREFIX}" --native-origins "${ORIGINS_PATH}" \
  --homebrew-info "${RUNTIME_DIR}/build-inputs/homebrew-llvm.json"

printf 'ClangSharp source: %s\n' "$(git -C "${SOURCE_DIR}" rev-parse HEAD)"
printf 'LLVM: %s\n' "$("${LLVM_PREFIX}/bin/llvm-config" --version)"
printf 'Runtime directory: %s\n' "${RUNTIME_DIR}"
printf 'Set BGCS_CLANG_RUNTIME_DIR to this directory when running BGCS.\n'
printf 'Set BGCS_OSX_X64_PACKAGE_RUNTIME_DIR to this directory when packing the full BGCS SDK and tool distribution.\n'
