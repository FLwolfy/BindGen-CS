# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

BindGen-CS is a Windows-first C/C++ to C# binding toolkit. It generates C# interop APIs for C-compatible libraries and can generate an ABI-stable C bridge for C++ APIs that cannot be called directly from .NET.

> **Status:** the Windows x64 safety and release gates pass. Unsupported C++ semantics are rejected with actionable diagnostics rather than guessed. The verified corpus and remaining platform/type boundaries are listed below and in the [acceptance specification](docs/acceptance.md).

## Goals

> Automatically generate safe bindings for supported C/C++ ABIs and standard-library types; strictly diagnose declarations missing ownership, allocator, or instantiation information and request only the minimum required configuration.

- One configuration file and one command for common libraries.
- Correct Windows MSVC ABI layouts and calling conventions.
- A shared binding IR consumed by C# and C bridge emitters.
- Readable APIs similar to `Inno.Native.*`, without editing generated files.
- Deterministic single-file output.
- Stable Runtime, generator, C++ bridge, and .NET tool NuGet packages.
- Compilation, ABI, runtime, package-consumer, and real-library regression tests.

## Quick start

```bash
dotnet tool install --global BindGen-CS
bindgen-cs init
bindgen-cs doctor
bindgen-cs validate
bindgen-cs generate
bindgen-cs build
```

The generated `bindgen.json` contains a beginner-friendly Windows configuration:

```json
{
  "Namespace": "Native.Bindings",
  "ApiName": "NativeApi",
  "LibName": "native",
  "EntryFiles": ["native.h"],
  "AllowedHeaders": [],
  "IncludeTransitivelyReferencedHeaders": true,
  "OutputPath": "Generated",
  "ParserKind": "C",
  "TargetArchitecture": "X64",
  "ImportType": "DllImport",
  "MergeGeneratedFilesToSingleFile": true,
  "SingleFileOutputName": "Bindings.cs",
  "GenerateRuntimeSource": false
}
```

Embedded usage remains available through the stable facade:

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
bool success = generator.GenerateConfigured();

if (!success)
{
    foreach (var diagnostic in generator.Messages)
        Console.Error.WriteLine(diagnostic);
}
```

Generate a configured C++ bridge with:

```bash
bindgen-cs bridge bridge.json
```

New applications can obtain the analyzed intermediate representation:

```csharp
using BGCS.Facade;
using BGCS.Intermediate;

BindingGenerationResult result = BindingGenerator.Generate("bindgen.json");
BindingModule? module = result.Module;
```

## Architecture

```text
BGCS
├─ Facade
│  ├─ CsCodeGenerator
│  └─ BindingGenerator
├─ Application
│  └─ BindingGenerationPipeline
├─ Configuration
│  ├─ ConfigLoader
│  ├─ ConfigComposer
│  ├─ ConfigValidator
│  └─ PresetResolver
├─ Analysis
│  ├─ DeclarationGraph
│  ├─ TypeAnalyzer
│  ├─ AbiLayoutAnalyzer
│  ├─ OwnershipAnalyzer
│  └─ OverloadPlanner
├─ Intermediate
│  ├─ BindingModule
│  ├─ BindingType
│  ├─ BindingFunction
│  └─ MarshallingPlan
├─ Emission
│  ├─ CSharpEmitter
│  ├─ CBridgeEmitter
│  ├─ RuntimeEmitter
│  └─ SingleFileComposer
└─ Output
   └─ OutputDirectoryTransaction
```

The compatibility facade now delegates orchestration to the application pipeline, and C#, Runtime, SingleFile, and C bridge output passes through explicit emitter boundaries. See the [architecture guide](docs/architecture.md).

## Acceptance target

A stable major release is accepted only when every category is independently measured between **8.5 and 9.0** and its mandatory gates pass.

| Category | Target | Mandatory evidence |
| --- | ---: | --- |
| Small C APIs | 8.5-9.0 | Generated C# compiles and runtime ABI tests pass without source edits |
| Medium/large C APIs | 8.5-9.0 | SDL3, miniaudio, and cimgui regenerate within budgets |
| Complex C ABI correctness | 8.5-9.0 | Mandatory Windows x64 layout, packing, union, bitfield, callback, and calling-convention tests |
| Ordinary C++ class bridge | 8.5-9.0 | Constructors, destructors, methods, overloads, inheritance casts, and exception boundary tests |
| Modern C++ | 8.5-9.0 | Explicit template instantiation, selected STL adapters, smart-pointer and virtual callback tests |
| Generated API quality | 8.5-9.0 | Reflection public API snapshots and zero manual generated-file patches |
| Beginner usability | 8.5-9.0 | Init/doctor/validate/generate/build workflow and actionable diagnostics |
| External architecture | 8.5-9.0 | Enforced one-way project dependencies and stable facade contracts |
| Internal architecture | 8.5-9.0 | Shared IR; analyzers and emitters independently tested; no generator god class |
| NuGet/testing/release | 8.5-9.0 | Clean consumer install, symbols, tool install, native/C# tests, deterministic packages |

The scoring rules, budgets, and pass/fail policy are normative in [docs/acceptance.md](docs/acceptance.md). Scores are not raised by documentation claims; `scripts/run-full-test-matrix.sh` writes the passing machine-readable result to `artifacts/acceptance/report.json` only after every mandatory layer succeeds.

| Measured category | Windows x64 score |
| --- | ---: |
| Small C APIs | 9.0 |
| Medium/large C APIs | 9.0 |
| Complex C ABI correctness | 9.0 |
| Ordinary C++ class bridge | 9.0 |
| Modern C++ | 9.0 |
| Generated API quality | 9.0 |
| Beginner usability | 9.0 |
| External architecture | 9.0 |
| Internal architecture | 9.0 |
| NuGet/testing/release | 9.0 |

Current local Windows real-library gates regenerate SingleFile bindings without generated-source edits and compile with zero C# warnings/errors:

| Library | Budget | Verified behavior |
| --- | ---: | --- |
| miniaudio split | 60s | Generate and compile |
| SDL3 umbrella header | 45s | Generate and compile |
| cimgui | 30s | Generate and compile |
| cimguizmo | 15s | Generate and compile |
| bgfx C99 | 30s | Generate and compile |
| bimg C++ | 15s | C bridge, clang++, C# rebind, API snapshot |

Synthetic C and generated C++ bridge DLL runtime invocation gates pass. Direct calls into the four upstream DLLs remain conditional on building those DLLs with their upstream build systems and are not claimed as verified.

## Packages

Public entry packages:

- `BGCS` — embeddable C/C++ to C# facade.
- `BGCS.Cpp2C` — C++ to C bridge generation.
- `BGCS.Runtime` — runtime primitives used by generated bindings.
- `BindGen-CS` — the `bindgen-cs` .NET tool.
- `BGCS.Intermediate` — dependency-free shared binding IR and diagnostics contracts.

Implementation packages (`BGCS.Core`, `BGCS.Language`, and `BGCS.CppAst`) are restored transitively. All release packages use one version and are package-consumer tested before publishing.

## Verification

```bash
./scripts/run-full-test-matrix.sh
./scripts/test-nuget-packages.sh
```

Windows C++ bridge tests use `BGCS_CPP2C_CXX` or discover `C:\Program Files\LLVM\bin\clang++.exe`.

## Wiki

- [Wiki index](docs/README.md)
- [Getting started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [Configuration guide](docs/configuration-guide.md)
- [Acceptance specification](docs/acceptance.md)
- [API reference](docs/api.md)
- [NuGet packages and exports](docs/packages.md)
- [Testing](docs/testing.md)
- [Publishing](docs/publish.md)

## License

BindGen-CS is licensed under the MIT License. See [LICENSE](LICENSE). Portions derived from CppAst/HexaGen retain their original notices.
