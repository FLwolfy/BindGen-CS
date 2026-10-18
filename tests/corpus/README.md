# Locked upstream corpus

The six acceptance products are miniaudio, SDL3, CImGui, CImGuizmo, bgfx and bimg.
`corpus.lock.json` records the exact commits and portable LF bytes needed for header
generation. `native-corpus.lock.json` separately locks the complete native source
materialization, including the exact Dear ImGui and ImGuizmo gitlinks. Restoration
uses a dedicated directory with `core.autocrlf=false`; an existing mismatched checkout
is rejected rather than rewritten. No generated binding is edited to pass a gate.

The full matrix first generates all declarations selected by each grouped configuration,
compiles the complete single-file binding with warnings as errors, and compares its
reviewed source/API snapshots. Variadic exclusions carried from the original corpus are
explicit configuration inputs. A new generator refusal fails the corpus and must be fixed
or reported; the runner does not silently remove declarations to produce a green result.

Snapshot schema 2 replaces only absolute quoted native `#include` paths beneath the
verified locked corpus root with `$BGCS_CORPUS/` in comparison hashes. This makes a
reviewed native bridge snapshot portable between checkout directories. Managed source,
native bodies, ordinary strings and external SDK includes remain byte-exact. Generated
files are never rewritten, and their artifact manifests retain the original byte hashes.

`upstream-limitations.json` locks every configured exclusion. bimg revision
`6c13a0c8a8908243df475dfcab276554458dcd13`, `include/bimg/bimg.h:460–465`,
declares `imageConvert(bx::AllocatorI *, bimg::TextureFormat::Enum, void const *, uint32_t)`
without an implementation in its complete locked source. The one exact overload is
excluded explicitly; the other `imageConvert` overloads remain selected. Reports and
snapshots disclose this upstream limitation. Its separate negative native build must
fail linking and preserve the prior complete owned output. No replacement implementation
is invented. A new or broadened exclusion fails policy validation unless its audited
policy is updated explicitly.

The SDL selection also excludes exactly `SDL_size_add_check_overflow_builtin` and
`SDL_size_mul_check_overflow_builtin`. These inline implementation helpers exist
only in compiler builtin branches and are not portable SDL DLL exports. The locked
policy checks their literal branch/function source, revision and header digest.
The portable `SDL_size_add_check_overflow` and `SDL_size_mul_check_overflow`
operations remain selected, including generated wrappers and native callable
assertions. This does not claim identical preprocessing across Clang and MSVC.

Actual native calls are a separate gate. `scripts/test-upstream-native.py` builds the
locked implementations in a disposable owned tree, runs an independent C/C++ comparison
probe, compiles a consumer of the complete generated binding, and compares the C# results
with the native probe's JSON. The fixtures cover version and scalar outputs, text-buffer
allocation/append/read/free, matrix compose/decompose, native vertex/texture layouts,
format metadata and pixel-buffer writes. bgfx uses its real Noop renderer and CPU-side
APIs; this gate does not claim GPU rendering certification.

Generated C helper sources are compiled into the configured upstream module, using
the complete owned artifact manifest. For example, SDL's inline header functions
need real generated exports in `SDL3-shared`; linking a helper-only module does not
make SDL's other symbols available through that module on Windows. bimg instead
builds the complete generated C++ bridge against its real upstream static library.

```sh
python scripts/corpus.py restore --root artifacts/corpus/headers
python scripts/corpus.py restore --root artifacts/real-library-native-corpus \
  --lock tests/corpus/native-corpus.lock.json
python scripts/test-corpus.py --dotnet /path/to/dotnet --group c
python scripts/test-corpus.py --dotnet /path/to/dotnet --group cpp
python scripts/test-upstream-native.py --dotnet /path/to/dotnet \
  --runtime src/BGCS.Runtime/bin/Release/net10.0/BGCS.Runtime.dll \
  --generation-receipt artifacts/acceptance/corpus-c.json \
  --generation-receipt artifacts/acceptance/corpus-cpp.json
```

Without generation receipts, the native runner can diagnose an upstream build/probe in
isolation. It explicitly records `bindingsInvoked=false`, which cannot satisfy the full
acceptance report. `--library` selects a finite diagnostic subset. Build intermediates
and native outputs are removed when the invocation finishes; receipts retain hashes,
native and managed results, command logs and bounded text reproduction inputs.
