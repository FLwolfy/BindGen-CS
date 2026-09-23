# BindGen-CS API reference

This document describes the current pre-release API. BindGen-CS has one C# generation architecture: parsing and analysis produce canonical Binding IR, and the IR-native emitter produces raw ABI declarations plus verified friendly overloads. Pre-release legacy generation APIs are intentionally absent.

## 1. Pipeline order

`CsCodeGenerator.GenerateConfigured()` executes:

1. resolve and validate the composed configuration;
2. parse the configured entry headers for the resolved target;
3. resolve the allowed-header closure;
4. run preprocessing and registered pre-patches;
5. analyze declarations into `BindingModule`;
6. validate safety and emitter coverage;
7. emit C# from canonical IR;
8. apply registered source post-patches;
9. compose optional single-file output and optional standalone Runtime source;
10. atomically replace the output and publish `BindingGenerationResult`.

Failures before the final commit preserve the last-good output. `bindgen-cs build` additionally compiles the generated source in a clean warnings-as-errors consumer.

## 2. Primary generation APIs

### `CsCodeGeneratorConfig`

The main configuration contract includes:

- inputs and outputs: `EntryFiles`, `IncludeFolders`, `OutputPath`, `MergeGeneratedFilesToSingleFile`, `SingleFileOutputName`;
- target: platform, architecture, ABI, target triple, sysroot, compiler and parser mode;
- import: `DllImport`, `LibraryImport`, or explicit function-table/native-context mode;
- filtering and naming: allowed/ignored declarations and mapping collections;
- safety: `MarshallingMappings`, ownership, allocator/cleanup, callback and async lifetime contracts;
- runtime: `GenerateRuntimeSource` and `RuntimeNamespace`;
- C# emission: `CSharpEmissionBackend.IntermediateRepresentation`, currently the only accepted value.

Use `ConfigLoader`, `ConfigValidator`, and `PresetResolver` for composed configuration. The current pre-release accepts exactly `ConfigVersion = 1`; it has no implicit old-schema migration.

### `CsCodeGenerator`

- `CsCodeGenerator.Create(configPath)` loads a file-backed generator.
- `GenerateConfigured(outputPath?)` is the normal embedded entry point.
- `AnalyzeConfigured()` returns parser, Binding IR, and safety diagnostics without replacing output.
- `Generate(...)` overloads support programmatic header lists and parser options.
- `LastResult` exposes the last structured result.
- `PatchEngine` registers pre- and post-patches.
- `GetMetadata()` / `SaveMetadata(path)` expose the current run's metadata for inspection and tooling; metadata is not a compatibility replay format.
- `Reset()` clears current run state.

`GeneratorBuilder` provides fluent configuration and patch registration. `BatchGenerator` orchestrates explicit multi-generation flows. Neither API carries a legacy emitter or metadata replay path.

## 3. Shared Binding IR

`BGCS.Intermediate` is the dependency-free contract package:

- `BindingModule`, `BindingType`, `BindingField`, `BindingEnumMember`;
- `BindingFunction`, `BindingParameter`, `BindingTypeReference`;
- `MarshallingPlan` and the ownership/encoding/lifetime enums;
- `BindingDiagnostic`, `BindingDiagnosticCatalog`, and `BindingEmissionException`;
- `BindingGenerationResult`, `IBindingEmitter`, and `EmissionContext`.

`BGCS.Facade.BindingGenerator.Generate(...)` returns a structured result. `CSharpEmitter` consumes `BindingModule` directly and fails with `BGCSCS001` before output creation when it cannot preserve a declaration. Alternate emitters and analyzers should depend on `BGCS.Intermediate` instead of old source-generation internals.

## 4. C++ bridge and native build

`Cpp2CCodeGenerator` creates a C ABI bridge for the explicitly supported C++ subset and exposes `LastResult`. Its bridge-specific generation-step API remains active because it lowers C++ AST semantics into C declarations; it is separate from the removed legacy C# generator.

`CppBridgeBuildManifest` records sources, includes, definitions, standard, linker inputs, target and output. Native build APIs include:

- `INativeBuildProvider` and providers for Clang/GNU, clang-cl, CMake, Meson, and MSBuild;
- `NativeBuildPlan` and `NativeBuildExecutor`;
- export verification through `nm` or `dumpbin`;
- `NativeAssetLayout` for `runtimes/<rid>/native/` packaging.

Unknown C++ specializations fail with a stable diagnostic rather than being treated as blittable.

## 5. Metadata and patching

`CsCodeGeneratorMetadata` contains outputs used by the current run, including function-table entries and data made available to post-patches. Its collection entry types support clone and merge for tooling, but do not promise compatibility across unpublished pre-release versions.

Patching interfaces are:

- `IPrePatch.Apply(PatchContext, CsCodeGeneratorConfig, List<string>, ParseResult)`;
- `IPostPatch.Apply(PatchContext, CsCodeGeneratorMetadata, List<string>)`.

Use `PatchContext.ReadFile` and `PatchContext.WriteFile` against staged relative paths. Post-patches execute before optional single-file composition and Runtime emission.

`PreProcessStep` remains the bounded parsed-input preprocessing hook. Cross-emitter semantic additions belong in IR analysis, marshalling plans, or an `IBindingEmitter`, not in a second C# generation path.

## 6. Runtime lifetime APIs

- `NativeCallback<T>` owns a shared callback lease.
- `NativeCallbackRegistry<TKey,TDelegate>` manages keyed callback leases.
- `NativeCallbackRegistration<TDelegate>` models native register/unregister and coordinates concurrent disposal, retry, and retained lifetime.
- `NativeAsyncOperation<TResult>` retains completion state and turns completion/cleanup failures into a terminal task result.
- `NativeCallbackExceptionBoundary` captures managed exceptions crossing callback boundaries.
- `NativeAotCallback` provides static `UnmanagedCallersOnly` thunks.

Allocator/deallocator pairs, unregister behavior, callback threading, and asynchronous completion are explicit contracts. Missing high-risk semantics produce safety diagnostics.

## 7. Minimal embedded generation

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
if (!generator.GenerateConfigured())
{
    foreach (var diagnostic in generator.Messages)
        Console.Error.WriteLine(diagnostic);
}
```

Programmatic configuration:

```csharp
using BGCS;

var config = new CsCodeGeneratorConfig
{
    ConfigVersion = CsCodeGeneratorConfig.CurrentConfigVersion,
    ApiName = "MyApi",
    Namespace = "My.Generated",
    LibName = "mylib",
    ImportType = ImportType.LibraryImport,
    GenerateExtensions = false
};

var generator = new CsCodeGenerator(config);
bool success = generator.Generate("include/native.h", "Generated");
```

## 8. Verification map

- configuration, parser and type mapping: `tests/BGCS.Tests`;
- IR-native output and consumer compilation: `tests/BGCS.Generation.Tests`;
- C++ bridge and native invocation: `tests/BGCS.Cpp2C.Tests`;
- Runtime ownership/lifetime/race behavior: `tests/BGCS.Runtime.Tests`;
- CLI, schemas and manifests: `tests/BGCS.Tool.Tests`;
- patch staging: `tests/BGCS.Patching.Tests`;
- public API gate: `scripts/test-public-api-compatibility.sh`;
- complete host acceptance: `scripts/run-full-test-matrix.sh`.

See [Testing](testing.md), [Acceptance](acceptance.md), and [Compatibility policy](compatibility-policy.md) for release gates and the post-1.0 lifecycle.
