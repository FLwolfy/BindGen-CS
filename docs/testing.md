# BGCS 3.0 Testing Workflow

[中文](testing.cn.md)

Tests distinguish model and generation regressions from native execution and installed
product evidence. A managed build on one host cannot certify another ABI, browser
deployment or package layout. Current completion is recorded in
[implementation status](implementation-status.cn.md); mandatory release jobs come from
`eng/acceptance/required-release-gates.json`.

## Prerequisites

- .NET 10 SDK/runtime; generated consumers use .NET 10 / C# 14.
- The locked host parser and worker closure. Target headers and native compilers are
  separate prerequisites.
- Python 3.10 or newer, a C++23 compiler and matching standard library.
- CMake 3.24 or newer and Ninja for CMake execution; CI locks Meson to 1.8.3.
- For browser execution, the selected .NET 10 SDK's `wasm-tools` and Chromium, Chrome
  or Edge. For desktop NativeAOT, the host compiler/linker prerequisites.

macOS Intel needs the matched supplement from `scripts/setup-macos-x64-clang-runtime.sh`.
Resource hashes participate in closure checks. An unrelated global LLVM installation
does not substitute for the locked parser. Native providers use compiler/SDK facts
compatible with the parsed target.

## Commands and evidence

| Scope | Command | Required evidence |
| --- | --- | --- |
| Regression suite | `dotnet test BindGen-CS.slnx -c Release` | All relevant tests, including failure/concurrency cases |
| Schema documentation | `python scripts/generate-config-docs.py --configuration Release --check` | Repository schema and both language catalogs match the built CLI |
| Browser interpreter | `python scripts/test-wasm-bindings.py` | All 74 actual checks across three import modes |
| Browser AOT | `python scripts/test-wasm-bindings.py --aot` | The same 74 checks after AOT publication |
| Desktop NativeAOT | `python scripts/test-native-aot-bindings.py` | All 113 checks, including independent aggregate oracles, in a published native executable |
| Installed products | `python eng/verify-packages.py --help` | Exact product set, installed consumers and complete invocation matrices |
| Performance | `scripts/test-performance-budget.sh` | Controlled 10,000-declaration cold/hot timings and memory budgets |
| Release inventory | `eng/acceptance/required-release-gates.json` | Every mandatory job from the same revision |

Fixture scripts normally build the necessary repository projects. In a shared checkout,
finish a serialized solution/Tool/worker build, then use `--no-build --configuration Release`
to stage closed outputs. `--tool-path` and `--runtime-path` select an exact installed set
together. Never overwrite assemblies while another consumer is using them.

Clean product verification restores the newly packed local NuGet products. It checks
embedded C/C++ generation, Contracts-only plugins, Runtime-only/package/source/single-file
consumers, exact worker/TaskHost closures, all six tool host resources, MSBuild opt-in and
default-off, owned clean/rebuild, multi-TFM and static project graphs. The Visual Studio
2026 job verifies `msbuild.exe` major 18 instead of substituting SDK-hosted MSBuild.
Full installed certification additionally runs NativeAOT smokes, the 113-call NativeAOT
matrix and both browser matrices. Explicit subsets retain `completeProductSet=false`.

## Independent invocation fixture

`tests/fixtures/NativeApi` supplies portable C17 source; `tests/fixtures/CppFacade` supplies
the independent C++23 header. The public unified CLI emits a typed bridge, reparses it
and generates three instance APIs: DllImport, LibraryImport and FunctionTable. The
consumer compiles exact managed artifacts; the harness never repairs generated source.

| Checks repeated for each import | Required result |
| --- | --- |
| Scalar/64-bit integer calls | Signed arithmetic and preserved high bits |
| Wide enum presentation | Fixed-width presentation and independently invoked native carrier |
| Native `long`/bool | Target width, signed arithmetic and boolean byte values |
| Record layout | Native size/offsets/pointer width and preserved context pointer |
| Mixed bitfields | Signed, unsigned and enum fields survive native mutation |
| Buffers and insufficient capacity | Exact values/count, and failure without writes |
| Original-address aliases | Native pointer equality and ordered ref/ref, in/out, record-field and Span/ref mutation |
| Opaque handles | Typed create/invoke/destroy and native allocation count |
| Native/null callbacks | Exact pointer/state/result/count, and explicit null rejection |
| Generated managed callbacks | Captured state through static userdata thunks, unchanged unsafe object pointers and original managed exception propagation |
| Release/null handles | No live allocations and null-safe destruction |
| C++ construction/methods | Status/error boundary and object state |
| Multiple inheritance | Adjusted base address, restored owner, rejected unrelated downcast |
| C++ destruction | Released owner and null-safe cleanup |

Each mode contributes 23 actual check labels. Five shared checks cover missing symbols,
owned provider disposal, failed table installation, borrowed provider ownership and
external pointer storage, giving **74 required checks**. A receipt must contain the
complete exact set after actual calls and assertions. NativeAOT adds 39 aggregate
checks across the three imports, for 113 total; each mode must match the independent
native C probe's 13 result shapes.

The original C++ header's implicit default constructor remains part of the test. No
explicit constructor is inserted to avoid unsupported generation. C source stays C17;
an unsupported managed presentation must be reported separately instead of changing
the native typedef into a compiler-specific enum extension.

The current fixture explicitly configures `api.enums` and `api.enumMappings`
for `WideFlags`, then checks the generated friendly enum and unchanged raw
`ulong` ABI. Bits 59/60/63 and the full UInt64 range round-trip through the
independent C17 implementation; no handwritten enum substitutes for generation.

FunctionTable uses `INativeModuleProvider`, `NativeModuleSession` and
`ExternalFunctionTableProvider`. Browser tests additionally invoke `FromStatic()`.
Leases preserve native cleanup and reject calls after context closure.

## Browser linking and execution

The harness queries the selected workload's real compiler/sysroot, links C and generated
C++ through `NativeFileReference`, publishes the browser app and invokes an owned
headless profile. Native module identity follows the declared filename: `api.c` registers
`api`, while `libapi.a` registers `libapi`. Static linking cannot assume `__Internal`
is correct for every managed runtime.

```bash
python scripts/test-wasm-bindings.py --dotnet /path/to/dotnet --browser /path/to/chromium
python scripts/test-wasm-bindings.py --aot --dotnet /path/to/dotnet
```

Receipts record actual browser/SDK/workload versions. Interpreter and AOT execute
separately on Windows x64, Linux x64 and Intel macOS. The neutral native-provider gate
independently verifies mixed C17/C++23 archives and relocatable objects; it sets exact
workload paths plus `BGCS_REQUIRE_WASM_NATIVE=1`, making missing prerequisites fail.

## Desktop NativeAOT and failure injection

```bash
python scripts/test-native-aot-bindings.py --dotnet /path/to/dotnet --compiler /path/to/clang
python scripts/test-native-aot-bindings.py --inject-native-error
python scripts/test-wasm-bindings.py --inject-native-error
```

NativeAOT builds the fixture library, publishes a standalone native consumer and executes
it. Both runners use `DisableRuntimeMarshalling` and the same unsafe ABI assertions.
Injection changes a copied scalar implementation; the real call must fail its assertion
and the gate. A compile failure cannot substitute for that required invocation failure.

Missing prerequisites, parse/native link failure, AOT compiler crashes, timeouts, wrong
pointer width and incomplete checks all fail. No dependency-based success fallback is
allowed. Compiler workarounds retain all 74 browser or 113 NativeAOT assertions and exact toolchain identity;
a successful diagnostic probe is not certification of the complete fixture.

## Receipts, cleanup and release truth

Each owned run retains grouped configs, native inputs, generated source, logs and
`report.json`. Receipts include revision, dirty state and actual tool/worker/runtime
hashes. Large owned `bin`, `obj`, published output, generator staging and browser profiles
are cleared on success/failure; `--keep-build-output` explicitly retains a diagnostic
scene. Cleanup never touches unrelated user input.

Semantic/ABI/cache, worker crash, publication recovery, path and Runtime lifetime cases
remain regression gates. Real-library corpus inputs use locked revisions and independent
source/API snapshots. Candidates require review rather than automatic promotion.
Historical 2.x reports and legacy test names do not constitute 3.0 receipts. Configured
CI jobs, local dirty-tree subsets and managed-only passes cannot establish release
readiness without the complete same-revision inventory.
