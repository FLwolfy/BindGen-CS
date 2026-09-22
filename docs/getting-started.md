# Getting Started

[Wiki](README.md) | [中文](getting-started.cn.md)

## Requirements

- Windows, Linux, or macOS on a supported x86/x64/Arm/Arm64 target.
- .NET SDK 9.0.
- LibClang is restored through BGCS packages.
- A C/C++ compiler driver is required for system-header discovery and generated C bridges. BindGen-CS discovers Clang/GNU drivers, the Windows LLVM installation, and the active macOS SDK; `BGCS_CC`, `BGCS_CPP2C_CXX`, `CC`, and `CXX` provide explicit overrides.

Choose the workflow before starting: use `bindgen.json` for a C ABI and `bridge.json` for C++ classes/templates. Do not generate direct P/Invoke declarations for C++ symbols without C linkage.

## Install the tool

```bash
dotnet tool install --global BindGen-CS
bindgen-cs --help
```

Use `dotnet tool update --global BindGen-CS` to upgrade a global installation. For a repository-local pinned tool, create a standard .NET tool manifest and install `BindGen-CS` into it.

## Generate a C binding

In a directory containing `native.h`, run:

```bash
bindgen-cs init native.h
bindgen-cs doctor
bindgen-cs validate bindgen.json
bindgen-cs inspect bindgen.json
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

Output is written to `Generated/Bindings.cs`. A successful run produces:

```text
project/
├─ native.h
├─ bindgen.json
└─ Generated/
   └─ Bindings.cs
```

`bindgen-cs build` compiles the generated source with warnings treated as errors, but does not build the upstream native library. `bindgen-cs diff` verifies that checked-in bindings are current without replacing them. Add the Runtime package to the consuming project:

```bash
dotnet add package BGCS.Runtime
```

If `GenerateRuntimeSource` is enabled, compile the generated `Runtime.cs` instead and do not reference duplicate Runtime types.

`init` writes portable paths relative to the generated configuration. For an ambiguous `.h`, select the workflow explicitly; use `--config` to place the configuration in another directory:

```bash
bindgen-cs init include/native.h --language c --config bindings/bindgen.json
bindgen-cs init include/library.h --language cpp --config bindings/bridge.json
```

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

Create the recommended configuration from a header:

```bash
bindgen-cs init include/library.hpp
```

`.hpp`, `.hh`, and `.hxx` inputs create `bridge.json`. Its core structure is equivalent to the following example.

Create `bridge.json`:

```json
{
  "ConfigVersion": 1,
  "EntryFiles": ["include/library.hpp"],
  "AllowedHeaders": ["include/library.hpp"],
  "OutputPath": "GeneratedBridge",
  "LanguageStandard": "c++23",
  "GenerateBuildManifest": true
}
```

Generate through the unified tool:

```bash
bindgen-cs bridge bridge.json
bindgen-cs native-build GeneratedBridge/bridge.manifest.json
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

The generated `bridge.manifest.json` is the deterministic handoff to native build automation. Paths are relative to the manifest when possible; it records the target, C++ standard, generated/original files, include directories, definitions, compiler/linker arguments, search directories, and libraries. `LanguageStandard` defaults to `c++23`; an existing `-std=` entry in `AdditionalArguments` remains an explicit compatibility override.

Inspect without executing, override the compiler, or select an explicit artifact path:

```bash
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --dry-run --json
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --compiler clang++ --output artifacts/libnative.dylib
```

`native-build` never invokes a command shell. Select `auto`, direct Clang/GNU, clang-cl, CMake, Meson, or MSBuild; every provider consumes the same manifest library search paths, link libraries, linker arguments, target triple, and sysroot where the backend supports them. Successful builds verify generated declarations against the binary export table by default.

Do not assume arbitrary template/STL types can be lowered automatically. See [Capabilities](capabilities.md) for explicit instances and supported adapters, and [Diagnostics](diagnostics.md) for rejection guidance.

## Multi-project workspaces

When a repository owns several native libraries, use a workspace to make configuration entry points and generation order explicit:

```bash
bindgen-cs workspace validate native/bindings/workspace.json
bindgen-cs workspace generate native/bindings/workspace.json
bindgen-cs workspace diff native/bindings/workspace.json
```

CI normally runs `workspace diff` to prove that configuration and checked-in bindings agree.

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
- **Need every configuration property:** run `bindgen-cs schema bindgen.schema.json`; `docs/config.md` lists only properties with dedicated entry regression tests.
