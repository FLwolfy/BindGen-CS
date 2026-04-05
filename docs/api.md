# BindGen-CS API Reference (BGCS)

This document focuses on BGCS runtime APIs and extension points: generator pipeline, metadata, patching, and function generation.

## 1. Pipeline Order

`CsCodeGenerator.Generate*` runs in this order:

1. `ConfigureCore` (initialize preprocess steps, generation steps, function generator)
2. `ParseFiles` (header parsing)
3. `PreProcessSteps`
4. `GenerationSteps`
5. `PatchEngine.ApplyPostPatches`
6. `RewriteRuntimeUsings`
7. Optional `MergeGeneratedFilesToSingleFile`
8. Optional `WriteStandaloneRuntimeFile` when `GenerateRuntimeSource=true`

Key detail: post-patches run before file merge and before `Runtime.cs` generation.

## 2. Core Types

## 2.1 `CsCodeGeneratorConfig`

Main behavior switchboard:

- output: `MergeGeneratedFilesToSingleFile`, `SingleFileOutputName`
- runtime: `GenerateRuntimeSource`
- import mode: `ImportType` (`DllImport` / `LibraryImport` / `FunctionTable`)
- generation toggles: `GenerateConstants/Enums/Functions/Types/Handles/Delegates/Extensions`
- filtering: `Allowed*`, `Ignored*`
- mappings/naming: `*Mappings`, `Known*`

Serialization/merge:

- `Load(path)`, `Save(path)`
- `Merge(baseConfig, MergeOptions)`

## 2.2 `CsCodeGenerator`

Main execution API:

- `Generate(...)` overloads for single/multi header and custom parser options
- step composition: `GetGenerationStep<T>()`, `AddGenerationStep(...)`, `OverwriteGenerationStep(...)`
- hooks: `PatchEngine`, `FunctionGenerator`
- metadata handoff: `CopyFrom(CsCodeGeneratorMetadata)`

## 2.3 `GeneratorBuilder` and `BatchGenerator`

- `GeneratorBuilder`: fluent setup, global/local patch registration, post-config callbacks
- `BatchGenerator`: batch orchestration with explicit `Generate(...)` and `Finish()`

## 3. Metadata APIs (`BGCS.Metadata`)

## 3.1 `CsCodeGeneratorMetadata`

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

## 3.2 Metadata Entry Types

- `GeneratorMetadataEntry`: base type (`Clone`, `Merge`)
- `MetadataListEntry<T>`: list-style entry
- `MetadataDictionaryEntry<TKey, TValue>`: dictionary-style entry
- `CsFunctionTableMetadata`: validates index/entrypoint consistency during merge

## 4. Patching APIs (`BGCS.Patching`)

Interfaces:

- `IPrePatch.Apply(PatchContext, CsCodeGeneratorConfig, List<string>, ParseResult)`
- `IPostPatch.Apply(PatchContext, CsCodeGeneratorMetadata, List<string>)`

`PatchContext` provides staged file operations:

- `ReadFile(relativePath)`
- `WriteFile(relativePath, content)`

Guideline: resolve target files from `files` list and use relative paths; avoid hardcoded output paths.

## 5. Function Generation APIs (`BGCS.FunctionGeneration`)

## 5.1 `FunctionGenerator`

Default composition:

- rules: `Ref`, `Span`, `String`, `Array`
- steps: `DefaultValue`, `ReturnVariation`, `StringReturn`

Customization:

- `AddRule`, `RemoveRule`, `OverwriteRule<T>`
- `AddStep`, `RemoveStep`, `OverwriteStep<T>`

## 5.2 Rules, Steps, and Parameter Writers

- `FunctionGenRule`: transforms each `CppParameter` into C# parameter forms
- `FunctionGenStep`: post-processes generated variations
- `IParameterWriter`: final marshalling code writer with priority-based ordering

## 6. Step Extension APIs

- `PreProcessStep`: `Configure`, `PreProcess`
- `GenerationStep`: `Configure`, `Generate`, `CopyToMetadata`, `CopyFromMetadata`, `Reset`

These are the main points for custom generator pipelines.

## 7. Runtime Strategy

- Generated bindings use `using BGCS.Runtime;`
- Runtime namespace is fixed to `BGCS.Runtime`
- `GenerateRuntimeSource=true` emits standalone `Runtime.cs`
- `GenerateRuntimeSource=false` emits no runtime source
- Generated runtime source is wrapped with `#if !BGCS_RUNTIME_EXTERNAL` guard

## 8. Test Mapping

- patch behavior: `tests/BGCS.Patching.Tests/*`
- generation pipeline + compile/runtime semantics: `tests/BGCS.Generation.Tests/*`
- core unit/parser interop: `tests/BGCS.Tests/*`
- full matrix entrypoint: `docs/testing.md`

## 9. End-to-End Examples

## 9.1 Minimal BGCS Generation

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

## 9.2 Single-File Bindings + Optional `Runtime.cs`

```csharp
using BGCS;

var cfg = new CsCodeGeneratorConfig
{
    ApiName = "MyApi",
    Namespace = "My.Generated",
    LibName = "mylib",
    ImportType = ImportType.FunctionTable,
    MergeGeneratedFilesToSingleFile = true,
    SingleFileOutputName = "Bindings.cs",
    GenerateRuntimeSource = true // false => no Runtime.cs emitted
};

var gen = new CsCodeGenerator(cfg);
gen.Generate("headers/api.h", "Output");
```

## 9.3 Register Pre/Post Patches

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

## 9.4 Metadata Reuse Across Runs

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

## 9.5 Custom Function Generation Strategy

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
