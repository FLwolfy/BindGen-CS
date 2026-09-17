# Architecture

[Wiki](README.md) | [中文](architecture.cn.md)

## Dependency rule

Dependencies point inward and never from analysis or intermediate models back to a facade or emitter:

```text
BGCS.Tool
  -> Facade
  -> Application
  -> Configuration + Analysis
  -> Intermediate
  -> BGCS.Core + BGCS.CppAst

Emission -> Intermediate
Output is shared infrastructure
BGCS.Runtime is independent of all generator assemblies
```

## Layers

### Facade

`CsCodeGenerator` preserves existing source compatibility. `BindingGenerator` is the concise new entry point returning `BindingGenerationResult`. A facade validates arguments and delegates; it must not contain AST traversal, ABI logic, source composition, or native build logic.

### Application

`BindingGenerationPipeline` owns the use-case sequence:

1. resolve and validate configuration;
2. parse native inputs;
3. build a declaration graph;
4. analyze ABI, types, ownership, and overload relationships;
5. produce one `BindingModule`;
6. run selected emitters into a staging directory;
7. compile/validate when requested;
8. atomically commit output;
9. return a structured result.

### Configuration

- `ConfigLoader`: source IO and config-relative paths.
- `ConfigComposer`: base configuration composition and cycle detection.
- `ConfigValidator`: complete validation before output mutation.
- `PresetResolver`: known-library and known-ABI defaults without copying large JSON files.

Configuration types describe intent. They must not write C# or traverse Clang AST nodes.

### Analysis

- `DeclarationGraph`: declarations and ABI dependencies.
- `TypeAnalyzer`: C/C++ type to IR type lowering.
- `AbiLayoutAnalyzer`: size, alignment, fields, arrays, unions, packing, and bitfields.
- `OwnershipAnalyzer`: borrowed/owned/transferred/caller-allocated semantics and string encoding.
- `OverloadPlanner`: pointer/count, capacity/written-count, strings, spans, callbacks, and two-call patterns.

Analyzers do not create files.

### Intermediate

`BindingModule`, `BindingType`, `BindingFunction`, and `MarshallingPlan` are the only input to final emitters. They contain resolved names and ABI facts but no Roslyn syntax, C++ source strings, filesystem paths, or mutable Clang state.

### Emission

- `CSharpEmitter`: raw imports and friendly APIs.
- `CBridgeEmitter`: ABI-stable wrappers for C++ declarations.
- `RuntimeEmitter`: optional standalone Runtime source.
- `SingleFileComposer`: syntax-aware deterministic composition.

Emitters do not infer ownership or layout. Missing analysis is an error, not an emitter heuristic.

### Output

`OutputDirectoryTransaction` stages all files on the destination volume. Existing output remains untouched until every required emitter and validator succeeds.

## Migration policy

The repository currently contains legacy AST-to-source generation steps. During migration:

- facade signatures remain compatible;
- every migrated path must first have an IR test and a generated compilation test;
- legacy and IR emitters must not both define the same declaration;
- no new feature may add logic to the `CsCodeGenerator` god class;
- a layer is considered migrated only after the facade delegates to it and old code is removed.

## Extension model

Long-term extensions register analyzers, policies, presets, and emitters through typed interfaces. Extensions receive immutable request/IR data and a scoped diagnostic sink. They must not depend on CLI types or mutate global current directories.
