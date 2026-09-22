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
          ├─ CSharpEmitter.EmitLegacy
          │    └─ GenerationStep implementations
          ├─ post-patch / SingleFile / optional Runtime
          └─ GeneratedOutputTransaction.commit
```

Important facts:

- `BindingModule` is a real analysis result used by structured results, safety diagnostics, and the new emitter API.
- Primary C# output currently calls `CSharpEmitter.EmitLegacy(...)`; that encapsulates but does not eliminate the AST/metadata-based `GenerationStep` path.
- `CSharpEmitter.Emit(BindingModule, EmissionContext)` is an IR-native path, but is not yet the default configured generation implementation.
- C++ bridging has its own analyzer and `CBridgeEmitter.EmitAst` path and also returns a `BindingModule`; it shares contracts with C# without every emission path being IR-only.
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

- `CSharpEmitter`: contains both IR-native `Emit` and the `EmitLegacy` adapter used by the current primary path.
- `RuntimeEmitter`: emits standalone runtime contracts from a module.
- `SingleFileComposer`: deterministic syntax-tree composition through Roslyn.
- `CBridgeEmitter`: emits C++ to C wrappers and currently still consumes AST-specific generation data.

### Output

`GeneratedOutputTransaction` / `OutputDirectoryTransaction` generate into staging directories. Final output is replaced only after generation and patching succeed, preserving last-good bindings on failure.

## Migration completion criteria

Primary C# generation is fully IR-native only when all of the following are true:

1. `BindingGenerationPipeline` calls `CSharpEmitter.Emit(BindingModule, ...)` as the default path.
2. Legacy `GenerationStep` implementations no longer determine public output semantics.
3. Metadata/patch capabilities become IR transforms or are explicitly constrained to source post-processing.
4. Real-library source/public-API snapshots and native tests remain unchanged.
5. Removing `EmitLegacy` does not change generated output.

The equivalent C++ bridge criterion is that the C Bridge emitter consumes a complete IR and the AST is confined to analysis.

## Extension model

New extensions should receive immutable configuration/requests, Binding IR, and scoped diagnostics. They must not depend on the CLI, change the global current directory, or hardcode one native library's names/layout into core. Library-specific facts belong in consumer configuration, preset composition, or an explicit custom adapter.
