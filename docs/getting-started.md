# Getting Started

[Wiki](README.md) | [中文](getting-started.cn.md)

## Requirements

- Windows, Linux, or macOS on a supported x86/x64/Arm/Arm64 target.
- .NET SDK 9.0.
- LibClang is restored through BGCS packages.
- A C/C++ compiler driver is required for system-header discovery and generated C bridges. BindGen-CS discovers Clang/GNU drivers, the Windows LLVM installation, and the active macOS SDK; `BGCS_CC`, `BGCS_CPP2C_CXX`, `CC`, and `CXX` provide explicit overrides.

## Install the tool

```bash
dotnet tool install --global BindGen-CS
bindgen-cs --help
```

For a repository-local pinned tool, create a standard .NET tool manifest and install `BindGen-CS` into it.

## Generate a C binding

Create a clean directory containing `native.h`, then run:

```bash
bindgen-cs init
bindgen-cs doctor
bindgen-cs validate
bindgen-cs generate
bindgen-cs build
```

Output is written to `Generated/Bindings.cs`. `bindgen-cs build` compiles the generated source with warnings treated as errors; `bindgen-cs diff` verifies that checked-in bindings are current without replacing them. Add the Runtime package to the consuming project:

```bash
dotnet add package BGCS.Runtime
```

If `GenerateRuntimeSource` is enabled, compile the generated `Runtime.cs` instead and do not reference duplicate Runtime types.

## Use an umbrella header

For headers such as SDL3's `SDL.h`, use:

```json
{
  "EntryFiles": ["include/SDL3/SDL.h"],
  "IncludeFolders": ["include"],
  "AllowedHeaders": [],
  "IncludeTransitivelyReferencedHeaders": true
}
```

Only declarations under entry-file directories and configured include folders are included. Compiler system headers remain excluded unless `ParseSystemIncludes` is enabled.

## Select C or C++ parsing

```json
{
  "ParserKind": "C"
}
```

Use `C` for C libraries and `Cpp` for C++ declarations or C headers requiring C++ extensions. C++ symbols without C linkage normally require `BGCS.Cpp2C`; direct P/Invoke cannot call mangled C++ member functions.

## Embedded facade

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
generator.LogToConsole();
if (!generator.GenerateConfigured())
    throw new InvalidOperationException("Binding generation failed.");
```

The last run exposes `LastResult.Module`, a shared IR used for inspection and future emitters.

## C++ bridge

Create `bridge.json`:

```json
{
  "EntryFiles": ["include/library.hpp"],
  "AllowedHeaders": ["include/library.hpp"],
  "OutputPath": "GeneratedBridge"
}
```

Generate through the unified tool:

```bash
bindgen-cs bridge bridge.json
```

Set `GenerateCSharpBindings=true` plus `CSharpNamespace`, `CSharpApiName`, `NativeLibraryName`, and `CSharpOutputPath` to emit both the native C bridge and C# bindings in this single command.

Embedded API:

```csharp
using BGCS.Cpp2C;

Cpp2CGeneratorConfig config = Cpp2CGeneratorConfig.Load("bridge.json");
Cpp2CCodeGenerator generator = new(config);
generator.Generate("include/library.hpp", "GeneratedBridge");
```

Compile generated `src/Classes.cpp` as a DLL with the generated `include` directory and original include directories. Then run BGCS against the generated C headers. Automatic native linking remains dependent on the original library build and is a separate acceptance gate.

## Do not edit generated files

Place custom behavior in:

- naming/type/function mappings;
- presets and policies;
- pre/post patches stored outside the output directory;
- handwritten partial types in a separate source directory.

The output transaction replaces the complete generated directory after success, so direct edits are intentionally not preserved.

## Troubleshooting

- **No declarations generated:** inspect `AllowedHeaders` and enable transitive user headers for umbrella headers.
- **Parsing takes too long:** disable macros/comments when not required and report the header as a performance regression; the real-library budgets are mandatory.
- **Unknown type:** add a `TypeMappings` entry or generate a C++ bridge specialization. Never map a non-trivial C++ value type directly to a blittable C# struct.
- **Duplicate Runtime types:** reference `BGCS.Runtime` or compile generated `Runtime.cs`, not both without `BGCS_RUNTIME_EXTERNAL`.
- **Native compiler missing:** set `BGCS_CC` and `BGCS_CPP2C_CXX` (or `CC`/`CXX`) to the appropriate compiler drivers.
