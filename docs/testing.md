# BindGen-CS Testing Workflow

The test system separates fast managed feedback from target-specific release evidence. A passing unit test on one host is not treated as proof for another native ABI.

## Prerequisites

- .NET SDK 10.0 and the .NET 9 runtime (`dotnet --version`, `dotnet --list-runtimes`)
- Clang/LibClang available for parser-dependent tests

On macOS Intel, ClangSharp 20.1.2 has no published native runtime package. Run `bash scripts/setup-macos-x64-clang-runtime.sh` on that host. Set `BGCS_CLANG_RUNTIME_DIR` to the printed directory for parser tests and `BGCS_OSX_X64_PACKAGE_RUNTIME_DIR` to that directory for packaging. The script checks that its LLVM 20 compiler can compile C++23 `std::expected`; the CI workflow performs the same bootstrap. On Linux, the CI matrix selects `g++` for the C++23 bridge tests because the runner's default Clang driver may use a standard library without `std::expected`.

## Choose the right test level

| Level | Command | Use it for |
| --- | --- | --- |
| Fast managed loop | `dotnet test BindGen-CS.sln -c Release` | Parser, configuration, analysis, emission, Runtime, and bridge regressions |
| Independent Wasm invocation | `python scripts/test-wasm-bindings.py` | Generate all three import modes, link BGCS-owned native C, and execute it in a browser |
| Real C libraries | `./scripts/test-real-libraries.sh` | Deterministic snapshots plus warning-free IR-native generation/compilation |
| Real C++ bridge | `./scripts/test-real-cpp-libraries.sh` | Bridge generation, native compiler validation, C# rebound, snapshots |
| NuGet/tool/native asset | `./scripts/test-nuget-packages.sh` | Deterministic pack, clean restore, tool install, RID asset load and native invocation |
| API compatibility | `./scripts/test-public-api-compatibility.sh` | Reviewed pre-release assembly baselines |
| Dependency policy | `./scripts/test-supply-chain-policy.sh` | NuGet advisory query and license inventory/deny policy |
| Performance | `./scripts/test-performance-budget.sh` | 10,000 declarations, cold generation and warm cache budgets |
| Release acceptance | `./scripts/run-full-test-matrix.sh` | Every mandatory gate and machine-readable report |

## One-command full matrix

```bash
./scripts/run-full-test-matrix.sh
```

Run the standalone commands from the table when iterating on one layer. Run the full matrix before treating the target as accepted.

The restore gates intentionally evaluate the MSBuild project graph on one node. This keeps `obj/project.assets.json` deterministic when the same checkout is mounted by different operating systems or architectures; compilation and native-library builds retain their normal parallelism.

The real-library matrix uses pinned upstream repositories fetched by `./scripts/setup-real-library-corpus.sh` into `artifacts/real-library-corpus/`. Set `BGCS_REAL_LIBRARY_ROOT` to an existing corpus at the same revisions if needed. For miniaudio, SDL3, cimgui, cimguizmo, and bgfx it regenerates the sole IR-native surface and checks target-specific deterministic source plus reflection public-API snapshots. These pinned API fixtures explicitly choose `StrictSafetySeverity=Warning` to preserve their reviewed broad-surface snapshots; the normal product default remains `SuppressFriendly`, which has separate regression tests. The full matrix prepares and requires these headers; the standalone script may set `REQUIRE_REAL_LIBRARIES=1` to make absence fatal. Generation times are reported but do not fail functional CI due to runner load or wall-clock changes. On a controlled performance runner, set `BGCS_ENFORCE_GENERATION_BUDGETS=1` to enforce the per-library limits in [acceptance](acceptance.md). The real C++ matrix additionally generates a bimg C bridge, validates it with the discovered C++ driver, feeds the generated C header back into BGCS, compiles the C# consumer, and verifies target-specific bridge/source/public-API snapshots.

For a host without a reviewed snapshot manifest, or whose generated output differs from one, real-library tests exit nonzero and write candidate SHA-256 files to `artifacts/acceptance/candidate-snapshots/`. CI also uploads the underlying generated files as `snapshot-inputs-<target>`. Review the files and public API before adding or changing the matching target-specific manifests under `tests/real-libraries/`; a candidate is never automatically promoted. The runner continues independent downstream gates to collect evidence, but emits no passing acceptance report until all snapshots match. A passing Arm64 baseline cannot stand in for x64 or another operating system. `bash scripts/test-snapshot-manifest.sh` tests LF/CRLF manifest handling and both fail-closed candidate paths.

These libraries are test inputs, not product-specific generation logic. Consumer repositories own their own configuration, generation workflow, generated files, and integration tests.

The full script runs all major BGCS capabilities in ordered layers:

1. Core libraries (`BGCS.Core`, `BGCS.CppAst`, `BGCS.Language`, `BGCS.Runtime`)
2. BGCS base/unit/parser logic (`BGCS.Tests`)
3. Patch-specific behavior (`BGCS.Patching.Tests`)
4. Generated output compile/runtime semantics (`BGCS.Generation.Tests`)
5. `BGCS.Cpp2C` generation, deterministic build manifests, native build-provider plans, C++ syntax validation, DLL linking, and runtime invocation
6. CLI behavior (`init`, JSON Schema, diagnostic catalog, and native-build plans)
7. End-to-end demo generation (`runtime-generated` + `runtime-notgenerated`)
8. NuGet dependency-closure restore, deterministic pack, tool installation, and native-RID consumer invocation
9. Public API baseline plus dependency license/vulnerability policy

On success it writes `artifacts/acceptance/report.json` and `report.md`, and retains target-specific copies under `artifacts/acceptance/reports/<target>/`. Report generation fails if any mandatory marker is missing. Windows x64, Linux x64, and macOS x64 reports must be generated from the same revision before maintenance status. See [Acceptance](acceptance.md) for the scoring contract.

Demo artifacts are emitted under:

- `demo/BGCS.Demo/bin/<Configuration>/generated/OutputRuntimeGenerated`
- `demo/BGCS.Demo/bin/<Configuration>/generated/OutputRuntimeNotGenerated`

Demo semantics:

- `runtime-generated`: single-file bindings + standalone `Runtime.cs` (`GenerateRuntimeSource=true`)
- `runtime-notgenerated`: single-file bindings only (`GenerateRuntimeSource=false`)

## Independent WebAssembly invocation

This test is owned entirely by BGCS. `tests/wasm/Native` supplies a small C API;
the runner generates bindings, builds a standalone .NET browser consumer, links the C
implementation through `NativeFileReference`, and invokes it in a headless Chromium browser.
No consumer repository, game engine, generated bindings committed elsewhere, or application
runtime is required.

Prerequisites:

- Python 3.10 or newer.
- .NET 9 SDK with its `wasm-tools` workload. Install the workload outside this checkout's
  SDK 10 selection, or explicitly select SDK 9 before `dotnet workload install wasm-tools`.
- Chrome, Chromium, or Edge. The browser runs with an isolated test profile.
- A working BGCS parser runtime for the authoring host. macOS Intel needs the bootstrap above.

```bash
python scripts/test-wasm-bindings.py
# For installations outside PATH:
python scripts/test-wasm-bindings.py --dotnet /path/to/dotnet --browser /path/to/chromium
```

The runner scopes SDK 9 to its generated consumer directory and queries the installed workload
for compiler and sysroot paths. Repository packaging keeps its existing SDK 10 selection.
`WasmBuildNative` links the C code; this fixture does not enable managed AOT compilation.
The generated `LibName` is `api`, matching the fixture's native input module. A different
consumer must use the module names required by its own linker/runtime; static linking does
not imply that the special name `__Internal` is supported everywhere. See the
[.NET native dependency workflow](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-native-dependencies?view=aspnetcore-9.0).

| Check, repeated for each import mode | Required result |
| --- | --- |
| Scalar parameters and return value | Exact signed arithmetic result |
| Record round trip | Matching size/offset, 32-bit pointer and `size_t`, preserved context pointer |
| Input/output buffers | Exact values and output count |
| Insufficient capacity | Failure with no writes and zero output count |
| Opaque handle | Typed create/invoke/destroy, tracked native ownership |
| Native-to-managed callback | Exact handle, arguments, state pointer, result, and invocation count |
| Null callback | Explicit failure without a callback |
| Release and null handles | Zero live native allocations; null release is safe |

`DllImport`, `LibraryImport`, and custom-context `FunctionTable` each execute these eight
checks. Missing-symbol resolution and exactly-once function-table context disposal add two
checks, for **26 total**. Function-table resolution is fixture-owned through the public
`INativeContext`; BGCS Runtime does not acquire a browser-specific symbol registry.

Each run retains its configuration, generated sources, native input, build logs, and
`report.json` under `artifacts/wasm-acceptance/<run-id>/`. Missing prerequisites, generation
or link failures, timeout, wrong pointer size, native exceptions, and incomplete check lists
fail the run. There is no dependency-driven skip or success fallback.

The CI `wasm-invocation` job repeats this test on Windows, Linux, and Intel macOS independently
of the desktop release matrix. Configured jobs are not evidence of completed runs. The
[local acceptance report](wasm-acceptance-2026-10-03.md) records the actual Windows/Edge result.
This fixture establishes its C ABI invocation scope; C++/STL Wasm semantics, managed AOT,
other browser engines, mobile devices, and distribution packaging require separate acceptance.

## Feature Coverage Mapping

`tests/BGCS.Tests` covers:

- Core unit and parser-interop behavior for BGCS

`tests/BGCS.Patching.Tests` covers:

- Patch infrastructure (`PatchEngine`) behavior
- Multi-stage patch matrix (pre/post, file create/modify, chaining)
- Single-file merge compatibility with post-patch behavior

`tests/BGCS.Generation.Tests` covers:

- Function generation pipeline and regression matrix
- Generated source compile correctness under matrix configurations
- Function-table/custom-context runtime behavior of generated code

`tests/BGCS.Cpp2C.Tests` covers:

- C++ to C bridge generation semantics and metadata flow
- deterministic, portable bridge build manifests and manifest validation
- shell-independent Clang/GNU build plans and actual host shared-library compilation
- configured STL lowerings for string, vector, span, array, map, set, optional, variant, expected, path, chrono, unique_ptr, and shared_ptr
- managed virtual callback proxy generation
- multiple-inheritance cast adjustment
- clang++ bridge DLL linking and Create/Invoke/Destroy/error-channel runtime calls
- retained callback unregister/dispose races and async exactly-once cleanup

`tests/BGCS.Tool.Tests` covers:

- portable, config-relative `init` output and C/C++ language selection
- strict C/C++ JSON Schema generation and compatibility opt-out
- stable diagnostic lookup through `explain`
- versioned manifest validation and shell-independent `native-build --dry-run` plans

## CI usage

The repository workflow runs the managed solution and complete acceptance jobs for Windows x64, Linux x64, and Intel macOS x64. A target is accepted only after its job emits its own report; a configured job or managed-only pass is not a target acceptance report.

The refactor-era all-red CI causes, fixes, local checks, and remaining runner evidence are recorded in [CI remediation (2026-09-24)](ci-remediation-2026-09-24.md).

CI can invoke:

```bash
SKIP_RESTORE_BUILD=1 ./scripts/run-full-test-matrix.sh
```

when restore/build are already completed in earlier steps.
