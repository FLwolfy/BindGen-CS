# Independent Wasm acceptance — 2026-10-03

[中文](wasm-acceptance-2026-10-03.cn.md) | [Wiki](README.md) | [Testing](testing.md#independent-webassembly-invocation)

## Result and scope

The BGCS-owned C ABI fixture passed **26 browser invocation checks** across `DllImport`,
`LibraryImport`, and custom-context `FunctionTable`. The full local solution regression
passed **716 tests in 11 projects**, with **zero failures and zero skips**. The Release
solution build reported **zero warnings and zero errors**.

This is standalone BGCS evidence. The native fixture, consumer, generator configuration,
symbol resolver, and assertions belong to this repository. No consumer application or
game engine participates in the test.

| Environment | Actual local value |
| --- | --- |
| Authoring host | Windows 11 x64, build 26200 |
| Browser | Edge 154.0.4258.53, headless, isolated profile |
| Wasm consumer SDK / runtime | .NET SDK 9.0.318 / runtime 9.0.20 |
| Native workload | .NET `wasm-tools`, Emscripten 3.1.56 |
| Output target | `emscripten-wasm32-emscripten`, 32-bit pointers |
| Managed execution | Mono WebAssembly runtime; managed AOT is not enabled |
| Parser | ClangSharp 20.1.2.4 / libclang 20.1.2, bundled Clang 20.1.8 builtin headers |
| Source-launch README smoke test | SDK 10.0.401, `init`, `generate`, and `build` all passed |

The parser's bundled builtin headers and the target SDK have separate roles. The fixture
uses the workload's compiler/sysroot for its target, while BGCS supplies the parser's
builtin resources. The builtin/native parser patch revisions above are recorded explicitly;
this report establishes the tested fixture rather than promising every SDK/header combination.

## What executes

The runner builds the real BGCS tool, generates three bindings surfaces, compiles their C#
consumer, compiles the C implementation, links it into the native Wasm runtime, serves the
published files over loopback HTTP, and waits for the browser's assertion report.

Each import mode checks:

1. Signed scalar parameters and return values.
2. Native/managed record size, context offset, 32-bit pointer and `size_t`, and round-trip field values.
3. Input and output buffers with exact values and output counts.
4. Insufficient-capacity failure without any buffer write.
5. Typed opaque-handle allocation and invocation.
6. Native-to-managed callback arguments, handle, state, result, and exactly-once invocation.
7. Null-callback rejection without invocation.
8. Native release, zero live allocations, and safe null-handle operations.

Missing-symbol resolution and exactly-once function-table context release complete the 26 checks.
Generated bindings are consumed directly; no generated code is patched to make the test pass.
`INativeContext` is the existing public symbol-resolution boundary. The fixture owns its symbol
catalog, and BGCS Runtime has no browser-specific registry or application-specific dependency.

## Generic defects repaired

| Defect | Repair | Regression evidence |
| --- | --- | --- |
| Borrowed `FunctionTable` storage was freed as if owned; repeated cleanup could also dispose a context repeatedly | Explicit storage ownership, bounds/disposal validation, zeroing added slots, idempotent cleanup | Caller storage survives cleanup; resize/disposal boundaries; context released once |
| A user namespace named `FunctionTable` shadowed the imported Runtime type | Fully qualify Runtime types using the configured Runtime namespace in every output layout | Compile checks for default/custom Runtime namespaces and default/custom symbol contexts; actual Wasm invocation |

These changes are generic runtime/emitter repairs. There is no Wasm-only branch in either repair,
and no new public API was needed. Runtime ownership behavior is documented in [API](api.md)
and [Packages](packages.md).

The acceptance application also needed the correct native module name (`api`, matching its
native input) and process ownership for Edge's compatibility launcher. Those are fixture/browser
harness settings. They do not change the generator's ABI or runtime loading policy.

## Failure verification and README

A separate temporary fixture deliberately changed native addition to return the wrong value.
The browser reported failure at `DllImport: scalar arguments/result.` with no passed checks.
The positive fixture and production source were preserved. This confirms that native execution
results are checked rather than accepting only successful generation/linking.

English and Chinese README files now explain headers, native implementations, and C# bindings
before introducing configuration. They include first-use steps, C/C++ workflows, import modes,
platform boundaries, and testing. The source-launch `init → generate → build` workflow was run
against a copied QuickStart header under SDK 10.0.401; the generated consumer compiled with
zero warnings/errors. A separate compiled-CLI smoke test under SDK 9 also passed.

## Evidence and reproduction

Use the [standalone command and prerequisites](testing.md#independent-webassembly-invocation).
Local evidence retained under ignored `artifacts/`:

| Evidence | Path relative to repository root |
| --- | --- |
| Complete positive Wasm run | `artifacts/wasm-acceptance/47946b86250f47139760c77a6a004f9a/report.json` |
| Native fault injection | `artifacts/wasm-negative/6c15c70adbed4843bf185419ac2350a2/negative-report.json` |
| All 11 regression reports/logs | `artifacts/wasm-managed-regression/` |
| Source-launch README smoke | `artifacts/readme-source-2b115e104f4a455a915ae1af39146ae5/` |
| Compiled-CLI README smoke | `artifacts/readme-quickstart/53f1fae2bede4d069feb59449e2d2842/` |

The Wasm run directory also retains native sources, generated bindings, consumer source,
configuration, compiler/sysroot query, and generation/publish logs. Artifacts are local evidence,
not committed products. No code was committed or pushed by this task.

## Remaining acceptance boundaries

Windows, Linux, and Intel macOS CI jobs now run this standalone test and upload their evidence.
Only the Windows/Edge local result above has been executed in this task. The other hosts,
Firefox/Safari, managed AOT, C++/STL Wasm semantics, mobile devices, and distribution packaging
still require their own reports. The desktop release matrix and its target-specific snapshots
remain independent gates. This report does not certify all platform/library combinations or
replace the [release acceptance specification](acceptance.md).

The specified completion sound `/System/Library/Sounds/Glass.aiff` is unavailable on this
Windows host, so it was not played.
