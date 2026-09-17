# BindGen-CS API Reference (BGCS)

This document focuses on BGCS runtime APIs and extension points: generator pipeline, metadata, patching, and function generation.

## 1. Pipeline Order

`CsCodeGenerator` is the compatibility facade. `BindingGenerationPipeline` owns the application sequence:

1. validate the composed configuration;
2. parse headers and report Clang diagnostics;
3. resolve the allowed-header closure;
4. run preprocess steps and pre-patches;
5. build shared `BindingModule` analysis data;
6. run configured generation/emission steps in a staging directory;
7. apply post-patches;
8. rewrite Runtime imports and remove empty generated declarations;
9. compose optional SingleFile output through Roslyn;
10. emit optional standalone Runtime source;
11. atomically commit output and publish `BindingGenerationResult`.

Post-patches run before SingleFile composition and Runtime emission. A failed stage does not replace last-good output.

## 2. Core Types

## 2.1 `CsCodeGeneratorConfig`

Main behavior switchboard:

- output: `OutputPath`, `MergeGeneratedFilesToSingleFile`, `SingleFileOutputName` (defaults to `Bindings.cs`)
- runtime: `GenerateRuntimeSource`, `RuntimeNamespace`
- import mode: `ImportType` (`DllImport` / `LibraryImport` / `FunctionTable`)
- generation toggles: `GenerateConstants/Enums/Functions/Types/Handles/Delegates/Extensions`
- filtering: `Allowed*`, `Ignored*`
- mappings/naming: `*Mappings`, `Known*`

Serialization/merge:

- `Load(path)`, `Save(path)`
- `Merge(baseConfig, MergeOptions)`

## 2.2 `CsCodeGenerator`

Main execution API:

- `GenerateConfigured(...)` for one-command config-driven generation with config-relative paths
- `AnalyzeConfigured()` for parse/IR validation without output replacement
- `LastResult` for `BindingModule`, diagnostics, success state, and emitted files
- `Generate(...)` overloads for single/multi header and custom parser options
- step composition: `GetGenerationStep<T>()`, `AddGenerationStep(...)`, `OverwriteGenerationStep(...)`
- hooks: `PatchEngine`, `FunctionGenerator`
- metadata handoff: `CopyFrom(CsCodeGeneratorMetadata)`

## 2.3 `GeneratorBuilder` and `BatchGenerator`

- `GeneratorBuilder`: fluent setup, global/local patch registration, post-config callbacks
- `BatchGenerator`: batch orchestration with explicit `Generate(...)` and `Finish()`

## 3. Shared IR and C++ Bridge Results

`BGCS.Intermediate` is a zero-BGCS-dependency contract assembly containing:

- `BindingModule`: one analyzed native module and target ABI;
- `BindingType` / `BindingField` / `BindingEnumMember`;
- `BindingFunction` / `BindingParameter`;
- `MarshallingPlan`: strategy, ownership, encoding, length/capacity relationships, and cleanup;
- `BindingDiagnostic` and `BindingGenerationResult`;
- `IBindingEmitter` and `EmissionContext`.

`BGCS.Facade.BindingGenerator.Generate(...)` returns a `BindingGenerationResult`. `Cpp2CCodeGenerator.LastResult` exposes the same result contract after C++ bridge generation. `BGCS.Emission.CSharpEmitter` and `BGCS.Cpp2C.Emission.CBridgeEmitter` both implement `IBindingEmitter`; the application pipelines also route compatibility generation passes through these emitter boundaries.

## 4. Metadata APIs (`BGCS.Metadata`)

## 4.1 `CsCodeGeneratorMetadata`

Holds generator state and cross-step outputs:

- constants/enums/functions/delegates/types/typedefs
- wrapped pointers
- function table (`CsFunctionTableMetadata`)

Main methods:

- `GetOrCreate<T>(key)`
- `TryGetEntry<T>(...)`
- `Merge(from, options)`
- `Clone(shallow = false)`
- `Save(path)`, `Load(path)`

## 4.2 Metadata Entry Types

- `GeneratorMetadataEntry`: base type (`Clone`, `Merge`)
- `MetadataListEntry<T>`: list-style entry
- `MetadataDictionaryEntry<TKey, TValue>`: dictionary-style entry
- `CsFunctionTableMetadata`: validates index/entrypoint consistency during merge

## 5. Patching APIs (`BGCS.Patching`)

Interfaces:

- `IPrePatch.Apply(PatchContext, CsCodeGeneratorConfig, List<string>, ParseResult)`
- `IPostPatch.Apply(PatchContext, CsCodeGeneratorMetadata, List<string>)`

`PatchContext` provides staged file operations:

- `ReadFile(relativePath)`
- `WriteFile(relativePath, content)`

Guideline: resolve target files from `files` list and use relative paths; avoid hardcoded output paths.

## 6. Function Generation APIs (`BGCS.FunctionGeneration`)

## 6.1 `FunctionGenerator`

Default composition:

- rules: `Ref`, `Span`, `String`, `Array`
- steps: `DefaultValue`, `ReturnVariation`, `StringReturn`

Customization:

- `AddRule`, `RemoveRule`, `OverwriteRule<T>`
- `AddStep`, `RemoveStep`, `OverwriteStep<T>`

## 6.2 Rules, Steps, and Parameter Writers

- `FunctionGenRule`: transforms each `CppParameter` into C# parameter forms
- `FunctionGenStep`: post-processes generated variations
- `IParameterWriter`: final marshalling code writer with priority-based ordering

## 7. Step Extension APIs

- `PreProcessStep`: `Configure`, `PreProcess`
- `GenerationStep`: `Configure`, `Generate`, `CopyToMetadata`, `CopyFromMetadata`, `Reset`

These are the main points for custom generator pipelines.

## 8. Runtime Strategy

`NativeCallback<T>` owns one shared, copy-safe callback lease. `NativeCallbackRegistry<TKey,TDelegate>` manages keyed registrations; replaced/unregistered leases remain retired until native unregister synchronization completes and `ReleaseRetired()` is called, preventing concurrent callback use-after-free. `NativeCallbackExceptionBoundary` converts managed exceptions to fallback results and stores the exception per thread. `NativeAotCallback` exposes static `UnmanagedCallersOnly` thunks without delegates or GCHandles. Delegate-based callbacks must use concrete delegate declarations with an explicit unmanaged calling convention.


- Generated bindings use `using {RuntimeNamespace};`
- `RuntimeNamespace` empty/whitespace defaults to `BGCS.Runtime`
- `GenerateRuntimeSource=true` emits standalone `Runtime.cs`
- `GenerateRuntimeSource=false` emits no runtime source
- Generated runtime source is wrapped with `#if !BGCS_RUNTIME_EXTERNAL` guard

## 9. Test Mapping

- patch behavior: `tests/BGCS.Patching.Tests/*`
- generation pipeline + compile/runtime semantics: `tests/BGCS.Generation.Tests/*`
- core unit/parser interop: `tests/BGCS.Tests/*`
- full matrix entrypoint: `docs/testing.md`

## 10. End-to-End Examples

## 10.1 Minimal BGCS Generation

```csharp
using BGCS;

var cfg = new CsCodeGeneratorConfig
{
    ApiName = "MyApi",
    Namespace = "My.Generated",
    LibName = "mylib",
    ImportType = ImportType.DllImport,
    GenerateExtensions = false
};

var gen = new CsCodeGenerator(cfg);
bool ok = gen.Generate("headers/api.h", "Output");
```

## 10.2 Single-File Bindings + Optional `Runtime.cs`

```csharp
using BGCS;

var cfg = new CsCodeGeneratorConfig
{
    ApiName = "MyApi",
    Namespace = "My.Generated",
    LibName = "mylib",
    ImportType = ImportType.FunctionTable,
    MergeGeneratedFilesToSingleFile = true,
    SingleFileOutputName = "MyApi.Bindings.cs",
    RuntimeNamespace = "My.Runtime", // optional, default BGCS.Runtime
    GenerateRuntimeSource = true // false => no Runtime.cs emitted
};

var gen = new CsCodeGenerator(cfg);
gen.Generate("headers/api.h", "Output");
```

## 10.3 Register Pre/Post Patches

```csharp
using BGCS;
using BGCS.Metadata;
using BGCS.Patching;

var cfg = CsCodeGeneratorConfig.Load("config.json");
var gen = new CsCodeGenerator(cfg);

gen.PatchEngine.RegisterPrePatch(new MyPrePatch());
gen.PatchEngine.RegisterPostPatch(new MyPostPatch());

gen.Generate("headers/api.h", "Output");

sealed class MyPrePatch : IPrePatch
{
    public void Apply(PatchContext context, CsCodeGeneratorConfig settings, List<string> files, ParseResult compilation)
    {
        // Example: mutate pre-generation staged files/config
    }
}

sealed class MyPostPatch : IPostPatch
{
    public void Apply(PatchContext context, CsCodeGeneratorMetadata metadata, List<string> files)
    {
        // Example: mutate generated staged files
    }
}
```

## 10.4 Metadata Reuse Across Runs

```csharp
using BGCS;
using BGCS.Metadata;

var cfg = CsCodeGeneratorConfig.Load("config.json");

var genA = new CsCodeGenerator(cfg);
genA.Generate("headers/a.h", "OutputA");
CsCodeGeneratorMetadata meta = genA.GetMetadata().Clone();

var genB = new CsCodeGenerator(cfg);
genB.CopyFrom(meta); // carry previous definitions to avoid duplicates
genB.Generate("headers/b.h", "OutputB");
```

## 10.5 Custom Function Generation Strategy

```csharp
using BGCS;
using BGCS.FunctionGeneration;

var cfg = CsCodeGeneratorConfig.Load("config.json");
var gen = new CsCodeGenerator(cfg);

var funcGen = FunctionGenerator.CreateDefault(cfg);
funcGen.OverwriteRule<FunctionGenRuleString>(new FunctionGenRuleString());
funcGen.OverwriteStep<DefaultValueGenStep>(new DefaultValueGenStep());
gen.FunctionGenerator = funcGen;

gen.Generate("headers/api.h", "Output");
```
