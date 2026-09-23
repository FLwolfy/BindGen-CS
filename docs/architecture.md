# Architecture

[简体中文](architecture.cn.md) | [Documentation index](README.md) | [Capability matrix](capabilities.md)

This document distinguishes the current implementation from the target architecture. They do not completely overlap: shared Binding IR, analysis, and emitter contracts exist, while primary C# generation still emits through compatibility `GenerationStep` implementations. Directory names are not a substitute for the actual data flow.

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
          ├─ CSharpEmissionBackend
          │    ├─ Compatibility → AstGenerationStepEmitter
          │    │                    └─ GenerationStep implementations
          │    └─ IntermediateRepresentation → CSharpEmitter(BindingModule)
          ├─ post-patch / SingleFile / optional Runtime
          └─ GeneratedOutputTransaction.commit
```

Important facts:

- `BindingModule` is a real analysis result used by structured results, safety diagnostics, and the new emitter API.
- `CSharpEmitter` is IR-only. Configured generation can select it explicitly through `CSharpEmissionBackend.IntermediateRepresentation`; `Compatibility` remains the default and calls the separately named `AstGenerationStepEmitter`.
- The IR-native path carries constants, aliases, delegates, opaque handles, enum underlying types, anonymous/nested records, bitfields, and DllImport/LibraryImport/function-table metadata. It now generates and warning-free compiles the raw ABI for miniaudio, SDL3, cimgui, cimguizmo, and bgfx. Friendly overload parity and every legacy public-API semantic are not complete, so the default-path migration is still open.
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

In the target state, emitters do not traverse mutable Clang AST state or depend on legacy generator metadata. The current codebase has the contracts and part of the emitter implementation, but migration is not complete.

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

## Layer responsibilities

### Facade

`CsCodeGenerator` preserves the compatibility API; `BGCS.Facade.BindingGenerator` returns `BindingGenerationResult`. The facade owns arguments and use-case entry points and should not accumulate more AST traversal or output composition.

### Configuration

- `ConfigLoader`: configuration input and relative-path context.
- `ConfigComposer`: BaseConfig merging and cycle detection.
- `ConfigValidator`: target, path, mapping, and output invariants before writes.
- `PresetResolver`: generic target/API/output defaults without library-specific hardcoding.

### Parsing

`BGCS.CppAst` uses Clang to build declaration/type/comment/token models. `CppTarget` and `CppToolchainDiscovery` supply target triples, system includes, and sysroots.

### Target and runtime portability

- `BGCS.CppAst` selects and preloads the RID-specific bundled Clang/ClangSharp runtime for Windows, macOS, and Linux x64/arm64 instead of relying on a host-global `libclang` name.
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

It is both a usable analysis result and the target model for legacy-emission migration. The existence of IR must not be confused with every emitter already being fully IR-native.

### Emission

- `CSharpEmitter`: IR-only `Emit` plus lossless-capability validation. `AstGenerationStepEmitter` owns the remaining compatibility path so parser-specific types cannot leak back into `CSharpEmitter`.
- `RuntimeEmitter`: emits standalone runtime contracts from a module.
- `SingleFileComposer`: deterministic syntax-tree composition through Roslyn.
- `CBridgeEmitter`: emits C++ to C wrappers and currently still consumes AST-specific generation data.

### Native build

- `CppBridgeBuildManifest`: versioned, deterministic, config-relative description of generated sources, target, toolchain inputs, and link inputs.
- `INativeBuildPipelineProvider`: maps a manifest to deterministic generated build inputs and argument-list process steps without shell quoting.
- Providers: direct Clang/GNU, clang-cl, CMake, Meson, and MSBuild.
- `NativeBuildExecutor`: bounded multi-step execution with captured output and no global working-directory mutation.
- `NativeExportInspector`: compares generated public C symbols with the built artifact export table.

Configuration-driven C++ generation also resolves headers, include directories, sysroots, compiler paths, outputs, and file-based `BaseConfig` chains from an explicit configuration directory. It does not change `Environment.CurrentDirectory`, so concurrent generators do not race through process-global path state.

## Cache and plugins

Configured C and C++ generation use an immutable SHA-256 output cache. The key includes generator identity, serialized configuration, parser arguments, resolved compiler identity/version, plugin/adapter fingerprints, and exact contents of discovered C/C++ inputs. Entries publish atomically, restore through the same output transaction as generation, and are isolated by key. Custom state that cannot be fingerprinted disables cache hits.

`BindingPluginContract` version 1 provides explicit assembly entry points and deterministic typed registrations. Plugin assemblies use an isolated dependency resolver while sharing host contracts; every assembly is preflighted and committed atomically, and its content hash/version enters the cache key. C++ plugins may register `ICppTypeAdapter` and `ICppCallableAdapter`; C# plugins may register additional `IBindingEmitter` services. Plugin assembly paths are explicit configuration, resolved relative to that configuration, and contract mismatches fail before generation.

### Output

`GeneratedOutputTransaction` / `OutputDirectoryTransaction` generate into staging directories. Final output is replaced only after generation and patching succeed, preserving last-good bindings on failure.

## Migration completion criteria

Primary C# generation is fully IR-native only when all of the following are true:

1. `BindingGenerationPipeline` calls `CSharpEmitter.Emit(BindingModule, ...)` as the default path.
2. Legacy `GenerationStep` implementations no longer determine public output semantics.
3. Metadata/patch capabilities become IR transforms or are explicitly constrained to source post-processing.
4. Real-library source/public-API snapshots and native tests remain unchanged.
5. Removing `AstGenerationStepEmitter` does not change generated output.

The equivalent C++ bridge criterion is that the C Bridge emitter consumes a complete IR and the AST is confined to analysis.

## Extension model

New extensions should receive immutable configuration/requests, Binding IR, and scoped diagnostics. They must not depend on the CLI, change the global current directory, or hardcode one native library's names/layout into core. Library-specific facts belong in consumer configuration, preset composition, or an explicit custom adapter.
