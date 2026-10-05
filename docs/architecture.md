# Architecture

Native build plans and execution contracts belong to `BGCS.Cpp2C.Build`. The five concrete compiler
and build-system providers belong to `BGCS.Cpp2C.Build.Providers` and reside in `Build/Providers`.
The CLI selects providers at its composition boundary.

[简体中文](architecture.cn.md) | [Documentation index](README.md) | [Capability matrix](capabilities.md)

The primary C# generator uses shared Binding IR exclusively. Pre-release compatibility emitters and old configuration migration were removed; architecture claims follow the actual data flow.

## Current data flow

BGCS.CppAst supplies its own embedded Clang 20 builtin resource headers. Compiler
driver discovery contributes the host SDK and standard library include paths,
excluding that driver's Clang resource directory. This prevents a newer system
LLVM installation from supplying builtin headers that the packaged native parser
cannot understand. Target sysroots and compiler selection remain explicit; Web
SDK selection belongs to the caller's toolchain. See the
[resource bundle provenance and license](../extern/clang-resource/README.md).

Compiler discovery drains stdout and stderr concurrently for resource, include and
fingerprint queries. Timed-out processes are terminated and observed before a query
returns; an unsuccessful probe remains explicitly unavailable.

`BGCS.Core.Execution.ProcessExecutor` owns the same process lifecycle for parser discovery,
native build steps, export inspection and generated C# compilation. Output pipes are drained
concurrently, standard input is closed, and timeout or cancellation retires the process tree
before returning. Native build pipeline deadlines use a monotonic clock.

`bindgen-cs validate architecture <repository-root>` checks the eight production projects,
allowed dependencies, the sole CLI executable, source ownership and parser-free neutral layers.
`bindgen-cs validate style <source-root> [--fix]` preserves authored call grouping and verifies
unchanged syntax tokens, including raw string contents, before writing declaration formatting.

Opaque handle wrappers remain the managed API. DllImport, LibraryImport and
FunctionTable all lower scalar handle arguments and results to the pointer-sized
`nint` native carrier, then share one typed adapter. This ABI rule applies to every
target and library; it does not add a browser branch to generated call wrappers.

```text
CLI / CsCodeGenerator / BindingGenerator
                 ↓
      Config load + composition + validation
                 ↓
       C/C++ parse → CppCompilation
                 ↓
        BindingGenerationPipeline
          ├─ preprocess / pre-patch
          ├─ DeclarationGraph
          ├─ BindingModuleAnalyzer → BindingModule
          ├─ StrictSafetyAnalyzer
          ├─ CSharpEmitter(BindingModule)
          ├─ post-patch / SingleFile / optional Runtime
          └─ OutputDirectoryTransaction.Commit
```

Important facts:

- `BindingModule` is a real analysis result used by structured results, safety diagnostics, and the new emitter API.
- `CSharpEmitter` is IR-only and is the sole configured C# output path.
- The IR-native path carries constants, aliases, delegates, opaque handles, enum underlying types, anonymous/nested records, bitfields, all import modes, and public raw/string/span/ref/out friendly overloads.
- The IR-native C# emitter validates lossless capability before writing and returns structured `BGCSCS001` diagnostics from configured generation for semantics it cannot preserve. It never falls back silently and failed emission leaves last-good output intact. Opaque storage whose fields are unavailable may be used through pointers, but by-value calls fail because size/alignment alone cannot prove platform ABI classification.
- C++ bridging uses one `CppBridgeGenerationPipeline`: `CppBridgeModuleAnalyzer` lowers the AST into frozen `CppBridgeModule` facts and source operations; `ICppBridgeEmitter` consumes that result. The direct AST emitter path has been removed. Generated C headers then enter the same C-to-C# Binding IR pipeline.
- C++ bridge output includes an optional target-specific build manifest. `INativeBuildPipelineProvider` converts it into shell-independent steps. Built-ins cover Clang/GNU, clang-cl, CMake, Meson, and MSBuild; binary export inspection uses `nm` or `dumpbin`.
- `BGCS.Core.Configuration.JsonConfigurationComposer` supplies one inheritance flow for C# and C++ configuration. File loaders preserve explicit JSON values; circular references include their source chain and fail before generation.
- CLI `bridge` prepares native and managed candidates through `OutputDirectorySetTransaction`. Managed projection carrier types come from the generated C aliases analyzed into Binding IR, rather than a handwritten ABI type table. Managed syntax validation runs before publication.
- Generation owns and disposes the Clang compilation it creates. Caller-supplied compilation inputs remain borrowed. Published IR does not retain the compilation or runtime lowering services.
- Compiler plans, manifests, export inspection results and package indexes own copied read-only sequences. Native package updates validate the existing index and publish a complete candidate while retaining the previous package on failure.
- CLI `build` creates a temporary .NET project after pipeline success for warning-as-error compilation. Compilation validation is not performed inside `BindingGenerationPipeline` itself.

## Target data flow

```text
Configuration → Parsing → Analysis → immutable BindingModule
                                         ↓
                  C# / Runtime / C Bridge / custom emitters
                                         ↓
                            Transactional Output
```

C# and C++ bridge emitters consume frozen Binding IR and Bridge IR. AST traversal and C++ lowering remain in their analysis stages.

## Dependency rules

Dependencies should point inward:

```text
BGCS.Tool
  → BGCS facade/application
  → configuration + analysis + emission
  → BGCS.Intermediate
  → BGCS.Core + BGCS.CppAst + BGCS.Language

BGCS.Runtime is independent of generator assemblies
BGCS.Intermediate depends on no other BGCS assembly
```

`Analysis` and `Intermediate` must not reference the CLI. Semantic facts do not retain Roslyn syntax, Clang cursors, services, or callbacks. Bridge lowering produces frozen target source units; the emission request supplies the actual output root.

Consumer applications own their binding configurations, native source, custom shims, generated output, and integration tests in their own repositories. BGCS owns only generic generation/runtime code and its independent, pinned upstream test corpus; no sibling application checkout is required by BGCS CI.

## Source and namespace ownership

Configuration lives under `BGCS.Configuration` and `BGCS.Cpp2C.Configuration`; public entry
points live under each generator's `Facade` namespace. Mutable declarations and overload planning
belong to `Analysis`, target and language conversion to `Conversion`, and source writing to
`Emission`. `BGCS.Core` owns reusable collections, text, IO, configuration composition and
process execution. `BGCS.Intermediate` owns the actual frozen model files without linked sources.

`BGCS.Runtime` deliberately uses its assembly namespace for its cohesive consumer API.
Its `Primitives`, `Interop` and `Utilities` directories group source responsibilities;
generated code imports one runtime namespace. This is a project namespace rule, independent
of the native target or managed deployment.

Pointer width belongs to `CppCompilation` and the analyzed declaration graph. Canonical pointer
aliases and configured variadic carriers preserve that target width. C++ template argument packs
are expanded by the parser into modeled type/integral arguments, so bridge lowering uses actual
target layouts rather than inferring ABI from type spelling. Analysis clears temporary AST
references in configuration on both success and failure; the frozen output retains none.

## Layer responsibilities

### Facade

`CsCodeGenerator` is the embeddable generator API; `BGCS.Facade.BindingGenerator` returns `BindingGenerationResult`. The facade owns arguments and use-case entry points and should not accumulate AST traversal or output composition.

`BindingGenerationPipeline` is the nonpublic application workflow shared by these entry points.
The facade returns its completed result and fails if the workflow violates that contract.
`IncrementalGenerationCache` acquires source and entry publication ownership in a stable order,
then verifies the exact file set, lengths and SHA-256 digests before restoring output.
Damaged entries are cache misses; overlapping output and cache trees are rejected.
The directory transaction exposes `stagingPath`; cache identities expose `value` and `inputFileCount`.

Installation and rollback share the internal Core rename boundary. Windows access or sharing failures
are retried for at most two seconds per move. Persistent denial retains the original failure and restores
the previous tree when installation has not completed. Temporary readers never justify dropping files,
unlinking retained lock inodes or accepting partial output. The public transaction contract is shared by all targets.

### Configuration

- `ConfigLoader`: configuration input and relative-path context.
- `ConfigComposer`: BaseConfig merging and cycle detection.
- `ConfigValidator`: target, path, mapping, and output invariants before writes.
- `PresetResolver`: generic target/API/output defaults without library-specific hardcoding.

### Parsing

`BGCS.CppAst` uses Clang to build declaration/type/comment/token models. `ClangTargetResolver` resolves neutral `INativeTargetProvider` requests; `CppToolchainDiscovery` supplies explicit host toolchain discovery.

### Target and runtime portability

- `BGCS.CppAst` selects and preloads RID-specific Clang/ClangSharp runtime assets where upstream packages exist. ClangSharp 20.1.2 does not publish macOS x64 native packages: Intel CI builds the matching native companion and LLVM 20 runtime with `scripts/setup-macos-x64-clang-runtime.sh`, then sets `BGCS_CLANG_RUNTIME_DIR`. Missing assets fail explicitly instead of silently changing parser versions.
- ABI classification owns target-specific primitive and compiler carrier rules. Linux Arm64 unsigned plain `char` and AAPCS64 `va_list` are modeled explicitly; SysV x64 array-decayed `va_list` remains a separate rule.
- `BGCS.Runtime.NativeLibrary` delegates module loading and export lookup to the .NET cross-platform loader, avoiding platform soname assumptions such as `libdl.so`.
- Workspace target subdirectories and target-specific snapshots/reports prevent one host's generated ABI from being compiled or accepted as another host's output.

### Analysis

- `DeclarationGraph`: declaration dependency ordering.
- `TypeAnalyzer`: native types to `BindingTypeReference`.
- `AbiLayoutAnalyzer`: size, alignment, fields, arrays, unions, and bitfield facts.

Bitfield bounds use Clang's absolute bit offset and declared width rather than the full size of each declared integer.
The emitter creates exact byte storage for adjacent fields. The `Bitfield` Span API preserves neighboring bits and sign-extends the declared range.
Signed, unsigned and enum fields can consequently share native storage, including packed records, without guessing an allocation unit.
The currently supplied native targets are little-endian; another byte order requires corresponding ABI lowering and native invocation acceptance.
- `OwnershipAnalyzer`: conservative marshalling/ownership defaults merged with explicit mappings.
- `OverloadPlanner`: pointer/count, capacity, and written-count relationships.
- `StrictSafetyAnalyzer`: unproven ownership, allocator, length, and callback lifetime.

Analyzers do not create final output files.

### Intermediate

`BGCS.Intermediate` is a dependency-free contract package containing `BindingModule`, types/functions/fields/parameters, `MarshallingPlan`, diagnostics, `IBindingEmitter`, and `EmissionContext`.

Binding IR is the only C# emitter input. The `Bridges` contracts provide frozen Bridge IR to the C++ emitter. Source files belong to this project rather than reverse-linked `Compile Link` items.

### Emission

- `CSharpEmitter`: the sole configured C# emitter, with IR-native `Emit`, friendly lowering, and lossless-capability validation.
- `RuntimeEmitter`: emits standalone runtime contracts from a module.
- `SingleFileComposer`: deterministic syntax-tree composition through Roslyn.
- `CBridgeEmitter`: writes C headers and C++ bridge source from frozen `CppBridgeModule`; it does not read AST state or repeat lowering.

### Native build

- `CppBridgeBuildManifest`: deterministic, current-format, config-relative description of generated sources, target, toolchain inputs, and link inputs.
- `INativeBuildPipelineProvider`: maps a manifest to deterministic generated build inputs and argument-list process steps without shell quoting.
- Providers: direct Clang/GNU, clang-cl, CMake, Meson, and MSBuild.
- `NativeBuildExecutor`: bounded multi-step execution with captured output and no global working-directory mutation.
- `NativeExportInspector`: compares generated public C symbols with the built artifact export table.

Configuration-driven C++ generation also resolves headers, include directories, sysroots, compiler paths, outputs, lowering recipes, native shims, and file-based `baseConfig` chains from an explicit configuration directory. It does not change `Environment.CurrentDirectory`, so concurrent generators do not race through process-global path state.

## Native call carriers

Generated C# call surfaces retain semantic types. Imports and unmanaged function pointers use explicit ABI carriers:
`nint` for opaque objects and the analyzed integral underlying type for by-value enums. Enum pointers retain their pointer types.
Generated adapters perform the conversion consistently across all three import modes and targets.
Custom-enum IR widths follow their configured integral underlying types.

The independent invocation fixture sends and returns integer and enum values above 32 bits.
Actual calls detect runtime signature errors that storage-size assertions cannot detect.

## Cache and plugins

Configured C and C++ generation use an immutable SHA-256 output cache. The key includes generator identity, serialized configuration, parser arguments, resolved compiler identity/version, plugin/lowering/shim fingerprints, and exact contents of discovered C/C++ inputs. Entries publish atomically, restore through the same output transaction as generation, and are isolated by key. Custom state that cannot be fingerprinted disables cache hits.

`BindingPluginContract.C_CURRENT_VERSION` provides a load-time revision handshake, explicit assembly entry points, and deterministic typed registrations. No earlier plugin contract is loaded. C++ plugins register `ICppTypeLowering`, `ICppCallableLowering`, and `ICppArtifactContributor`; the same registry carries built-ins, declarative recipes, and plugin lowerings. Plugin assemblies use an isolated dependency resolver and atomic registration, while assembly content hashes, versions, and lowering fingerprints enter the cache key.

### Output

`GeneratedOutputTransaction` / `OutputDirectoryTransaction` generate into staging directories. Final output is replaced only after generation and patching succeed, preserving last-good bindings on failure.

## Completion criteria

The C# path is complete when `BindingGenerationPipeline` calls `CSharpEmitter.Emit(BindingModule, ...)`, metadata/patch behavior is either an IR transform or explicit source post-processing, and feature-specific source/API/compile tests plus the complete real-library matrix pass. No compatibility emitter participates in configured output.

The equivalent C++ bridge criterion is that the C Bridge emitter consumes a complete IR and the AST is confined to analysis.

## Extension model

New extensions should receive immutable configuration/requests, Binding IR, and scoped diagnostics. They must not depend on the CLI, change the global current directory, or hardcode one native library's names/layout into core. Library-specific facts belong in declarative lowerings, typed lowering plugins, or explicit C ABI shims. See the [final lowering architecture](lowering.md).

## Consumer independence and WebAssembly targets

BGCS owns parsing, ABI analysis, lowering, binding emission, and its own fixtures and acceptance reports. Consumers own their native facades, SDK selection, application link graph, deployment, and runtime integration. A consumer's successful application build is evidence for that consumer; it does not replace BGCS's standalone target acceptance. Capability claims here refer only to BGCS-owned evidence.

`emscripten-wasm32-emscripten` selects the Clang triple `wasm32-unknown-emscripten` and its C/C++ ABI. BGCS models it through the same target/configuration/IR/emitter contracts as desktop targets. It does not create a separate generator for each library, install an SDK, or implement browser rendering, input, or application startup.

The authoring host and target are separate. Emscripten supports Windows, macOS, and Linux authoring hosts and emits WebAssembly; `wasm32` describes the target's 32-bit pointer address model. Other WebAssembly environments, such as WASI, are distinct targets rather than aliases for Emscripten. See [Emscripten installation](https://emscripten.org/docs/getting_started/downloads.html), [WebAssembly output](https://emscripten.org/docs/compiling/WebAssembly.html), and [Clang cross-compilation](https://clang.llvm.org/docs/CrossCompilation.html).

BGCS-owned target tests cover the triple and record layout; emitter tests cover opaque handles in all three import modes. The standalone fixture now executes generated DllImport, LibraryImport, and FunctionTable bindings against BGCS-owned C code inside a browser. The 2026-10-04 Windows x64 NativeAOT and Edge/Wasm interpretation runs each passed 47 checks, including C++ bridge invocation. Managed Wasm AOT, wider host/browser coverage, and distribution packaging retain separate evidence gates. See [invocation workflow](testing.md#independent-webassembly-invocation), [local acceptance](wasm-acceptance-2026-10-03.md), and [target evidence](capabilities.md#target-evidence).

## Explicit target composition

`BGCS.Core.Targeting` owns `NativeTargetId`, immutable target/toolchain descriptors, requests,
and `INativeTargetProvider`. Core has no parser reference. `BGCS.CppAst.Targeting` resolves
exactly one provider, maps the resolved descriptor into Clang arguments, and keeps SDK headers
separate from bundled builtin resources. Built-in Windows, Unix, Apple and Emscripten providers
can be replaced with an explicit provider set; an empty set does not enable defaults.

An Emscripten provider applies the selected sysroot's libc include directory. No Windows path
or application runtime is inferred. The authoring compiler, Clang builtin bundle, target SDK,
ABI, native linking, and managed deployment remain separate concerns. The `host` alias is
resolved once before provider dispatch. Apple device and simulator IDs stay distinct.
