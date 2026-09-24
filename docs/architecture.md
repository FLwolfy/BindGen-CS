# Architecture

[简体中文](architecture.cn.md) | [Documentation index](README.md) | [Capability matrix](capabilities.md)

The primary C# generator uses shared Binding IR exclusively. Pre-release compatibility emitters and old configuration migration were removed; architecture claims follow the actual data flow.

## Current data flow

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
          └─ GeneratedOutputTransaction.commit
```

Important facts:

- `BindingModule` is a real analysis result used by structured results, safety diagnostics, and the new emitter API.
- `CSharpEmitter` is IR-only and is the sole configured C# output path.
- The IR-native path carries constants, aliases, delegates, opaque handles, enum underlying types, anonymous/nested records, bitfields, all import modes, and public raw/string/span/ref/out friendly overloads.
- The IR-native C# emitter validates lossless capability before writing and returns structured `BGCSCS001` diagnostics from configured generation for semantics it cannot preserve. It never falls back silently and failed emission leaves last-good output intact. Opaque storage whose fields are unavailable may be used through pointers, but by-value calls fail because size/alignment alone cannot prove platform ABI classification.
- C++ bridging has its own analyzer and `CBridgeEmitter.EmitAst` path and also returns a `BindingModule`; it shares contracts with C# without every emission path being IR-only.
- C++ bridge output includes an optional versioned build manifest. `INativeBuildPipelineProvider` converts it into shell-independent steps. Built-ins cover Clang/GNU, clang-cl, CMake, Meson, and MSBuild; binary export inspection uses `nm` or `dumpbin`.
- CLI `build` creates a temporary .NET project after pipeline success for warning-as-error compilation. Compilation validation is not performed inside `BindingGenerationPipeline` itself.

## Target data flow

```text
Configuration → Parsing → Analysis → immutable BindingModule
                                         ↓
                  C# / Runtime / C Bridge / custom emitters
                                         ↓
                            Transactional Output
```

The C# target state is implemented: its emitter does not traverse mutable Clang AST state. C++ bridge generation still has an explicit AST-specific lowering boundary for constructs not yet represented by shared IR.

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

`Analysis` and `Intermediate` must not reference the CLI. IR contracts should not contain Roslyn syntax, generated source strings, filesystem paths, or mutable Clang cursors.

Consumer applications own their binding configurations, native source, custom shims, generated output, and integration tests in their own repositories. BGCS owns only generic generation/runtime code and its independent, pinned upstream test corpus; no sibling application checkout is required by BGCS CI.

## Layer responsibilities

### Facade

`CsCodeGenerator` is the embeddable generator API; `BGCS.Facade.BindingGenerator` returns `BindingGenerationResult`. The facade owns arguments and use-case entry points and should not accumulate AST traversal or output composition.

### Configuration

- `ConfigLoader`: configuration input and relative-path context.
- `ConfigComposer`: BaseConfig merging and cycle detection.
- `ConfigValidator`: target, path, mapping, and output invariants before writes.
- `PresetResolver`: generic target/API/output defaults without library-specific hardcoding.

### Parsing

`BGCS.CppAst` uses Clang to build declaration/type/comment/token models. `CppTarget` and `CppToolchainDiscovery` supply target triples, system includes, and sysroots.

### Target and runtime portability

- `BGCS.CppAst` selects and preloads RID-specific Clang/ClangSharp runtime assets where upstream packages exist. ClangSharp 20.1.2 does not publish macOS x64 native packages: Intel CI builds the matching native companion and LLVM 20 runtime with `scripts/setup-macos-x64-clang-runtime.sh`, then sets `BGCS_CLANG_RUNTIME_DIR`. Missing assets fail explicitly instead of silently changing parser versions.
- ABI classification owns target-specific primitive and compiler carrier rules. Linux Arm64 unsigned plain `char` and AAPCS64 `va_list` are modeled explicitly; SysV x64 array-decayed `va_list` remains a separate rule.
- `BGCS.Runtime.NativeLibrary` delegates module loading and export lookup to the .NET cross-platform loader, avoiding platform soname assumptions such as `libdl.so`.
- Workspace target subdirectories and target-specific snapshots/reports prevent one host's generated ABI from being compiled or accepted as another host's output.

### Analysis

- `DeclarationGraph`: declaration dependency ordering.
- `TypeAnalyzer`: native types to `BindingTypeReference`.
- `AbiLayoutAnalyzer`: size, alignment, fields, arrays, unions, and bitfield facts.
- `OwnershipAnalyzer`: conservative marshalling/ownership defaults merged with explicit mappings.
- `OverloadPlanner`: pointer/count, capacity, and written-count relationships.
- `StrictSafetyAnalyzer`: unproven ownership, allocator, length, and callback lifetime.

Analyzers do not create final output files.

### Intermediate

`BGCS.Intermediate` is a dependency-free contract package containing `BindingModule`, types/functions/fields/parameters, `MarshallingPlan`, diagnostics, `IBindingEmitter`, and `EmissionContext`.

It is both a usable analysis result and the only input to C# emission. The C++ bridge retains a separate, explicit AST-specific lowering boundary.

### Emission

- `CSharpEmitter`: the sole configured C# emitter, with IR-native `Emit`, friendly lowering, and lossless-capability validation.
- `RuntimeEmitter`: emits standalone runtime contracts from a module.
- `SingleFileComposer`: deterministic syntax-tree composition through Roslyn.
- `CBridgeEmitter`: emits C++ to C wrappers and currently still consumes AST-specific generation data.

### Native build

- `CppBridgeBuildManifest`: versioned, deterministic, config-relative description of generated sources, target, toolchain inputs, and link inputs.
- `INativeBuildPipelineProvider`: maps a manifest to deterministic generated build inputs and argument-list process steps without shell quoting.
- Providers: direct Clang/GNU, clang-cl, CMake, Meson, and MSBuild.
- `NativeBuildExecutor`: bounded multi-step execution with captured output and no global working-directory mutation.
- `NativeExportInspector`: compares generated public C symbols with the built artifact export table.

Configuration-driven C++ generation also resolves headers, include directories, sysroots, compiler paths, outputs, lowering recipes, native shims, and file-based `BaseConfig` chains from an explicit configuration directory. It does not change `Environment.CurrentDirectory`, so concurrent generators do not race through process-global path state.

## Cache and plugins

Configured C and C++ generation use an immutable SHA-256 output cache. The key includes generator identity, serialized configuration, parser arguments, resolved compiler identity/version, plugin/lowering/shim fingerprints, and exact contents of discovered C/C++ inputs. Entries publish atomically, restore through the same output transaction as generation, and are isolated by key. Custom state that cannot be fingerprinted disables cache hits.

`BindingPluginContract` version 2 provides explicit assembly entry points and deterministic typed registrations. The old adapter services were deleted and have no compatibility wrapper. C++ plugins register `ICppTypeLowering`, `ICppCallableLowering`, and `ICppArtifactContributor`; the same registry carries built-ins, declarative recipes, and plugin lowerings. Plugin assemblies use an isolated dependency resolver and atomic registration, while assembly content hashes, versions, and lowering fingerprints enter the cache key.

### Output

`GeneratedOutputTransaction` / `OutputDirectoryTransaction` generate into staging directories. Final output is replaced only after generation and patching succeed, preserving last-good bindings on failure.

## Completion criteria

The C# path is complete when `BindingGenerationPipeline` calls `CSharpEmitter.Emit(BindingModule, ...)`, metadata/patch behavior is either an IR transform or explicit source post-processing, and feature-specific source/API/compile tests plus the complete real-library matrix pass. No compatibility emitter participates in configured output.

The equivalent C++ bridge criterion is that the C Bridge emitter consumes a complete IR and the AST is confined to analysis.

## Extension model

New extensions should receive immutable configuration/requests, Binding IR, and scoped diagnostics. They must not depend on the CLI, change the global current directory, or hardcode one native library's names/layout into core. Library-specific facts belong in declarative lowerings, typed lowering plugins, or explicit C ABI shims. See the [final lowering architecture](lowering.md).
