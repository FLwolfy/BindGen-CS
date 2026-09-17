# NuGet Packages and Public APIs

[中文](packages.cn.md) | [Wiki](README.md) | [API reference](api.md)

## Which package should I install?

| Goal | Install | Notes |
| --- | --- | --- |
| Use the command line | `dotnet tool install --global BindGen-CS` | Provides `bindgen-cs`; do not add it to an application project. |
| Embed C/C++ → C# generation | `BGCS` | Main facade, configuration, analysis, C# emission, patching, and compatibility generation passes. |
| Generate a C ABI bridge for C++ | `BGCS.Cpp2C` | Add this when embedding bridge generation; the CLI tool already carries it. |
| Compile generated bindings | `BGCS.Runtime` | Install in the application that consumes generated code, unless standalone Runtime emission is enabled. |
| Write an emitter or IR tool | `BGCS.Intermediate` | Dependency-free shared contracts; does not require Clang, Roslyn, or Runtime. |
| Build parser/tooling extensions | `BGCS.CppAst`, `BGCS.Core`, `BGCS.Language` | Advanced implementation packages; normally restored transitively. |

All release packages are version-aligned and validated in a clean NuGet consumer before publishing.

## `BindGen-CS` tool

Commands exported by the package:

```text
init, doctor, validate, inspect, generate, build, diff, schema, bridge, version
```

The tool depends on `BGCS` and `BGCS.Cpp2C` inside its isolated tool installation. Generated application code does not depend on the tool package.

## `BGCS`

Primary public APIs:

- `CsCodeGenerator`, `CsCodeGeneratorConfig`, `GeneratorBuilder`, `BatchGenerator`;
- `BGCS.Facade.BindingGenerator`;
- `BindingGenerationPipeline`;
- `ConfigLoader`, `ConfigValidator`, `PresetResolver`;
- `DeclarationGraph`, `BindingModuleAnalyzer`, `TypeAnalyzer`, `AbiLayoutAnalyzer`, `OwnershipAnalyzer`, `OverloadPlanner`;
- `CSharpEmitter`, `RuntimeEmitter`, `SingleFileComposer`;
- generation/preprocess steps, function-generation rules and parameter writers;
- patching and generator metadata APIs;
- `VariadicFunctionVariant` for explicit promoted C variadic signatures.

Direct dependencies include `BGCS.Intermediate`, `BGCS.Core`, `BGCS.Language`, `BGCS.CppAst`, and Roslyn. `CommandLineParser` is no longer a dependency.

## `BGCS.Cpp2C`

Primary public APIs:

- `Cpp2CCodeGenerator`, `Cpp2CGeneratorConfig`;
- `BGCS.Cpp2C.Emission.CBridgeEmitter`;
- bridge generation-step extension points;
- C/C++ type lowering helpers and generated-function metadata.

Current bridge capabilities include classes, constructors/destructors, instance/static methods, namespace free functions, exception channels, explicit class-template specializations, and pointer-adjusting inheritance casts.

## `BGCS.Intermediate`

This package has no dependency on another BGCS assembly. It exports:

- `BindingModule`;
- `BindingType`, `BindingField`, `BindingEnumMember`, `BindingTypeReference`;
- `BindingFunction`, `BindingParameter`;
- `MarshallingPlan`;
- `BindingGenerationResult`, `BindingDiagnostic`;
- `IBindingEmitter`, `EmissionContext`;
- supporting type/function/direction/ownership/encoding/marshalling enums.

Use it for analyzers, API diff tools, alternate language emitters, and build-system integrations that should not load Clang or Roslyn.

## `BGCS.Runtime`

Primary public runtime surface:

- `Bool8`, `Bool32`;
- `Pointer<T>`, `ConstPointer<T>`;
- `Atomic<T>`;
- `NativeCallback<T>`;
- `NativeCallbackRegistry<TKey,TDelegate>`;
- `INativeContext`, `NativeLibraryContext`;
- `NativeLibrary`, `LibraryLoader`, `TargetPlatform`, `ResolvePathHandler`;
- `NativeNameAttribute`, `SourceLocationAttribute`, `NativeNameType`;
- `Utils` allocation, UTF-8/UTF-16, pointer, and array helpers.

Generated bindings normally reference this package. `GenerateRuntimeSource=true` instead emits a guarded standalone `Runtime.cs`; do not combine embedded Runtime and the package without defining `BGCS_RUNTIME_EXTERNAL`.

## Advanced implementation packages

- `BGCS.CppAst`: ClangSharp-based C/C++ AST model, parser options, visitors, declarations, types, expressions, comments, tokens, and diagnostics.
- `BGCS.Core`: output transactions, code writers, mappings, metadata helpers, logging, collections, and path/file utilities.
- `BGCS.Language`: lexer, parser, preprocessor expression infrastructure, syntax nodes, diagnostics, and analyzers.

Applications should not install these individually unless they directly consume those extension APIs.

## Current limitations

- Verified modern C++ adapters cover `std::string`, input `std::span<T>`, blittable `std::optional<T>`, and ownership-transferring `std::unique_ptr<T>`, plus configured pure-virtual callback proxies. `std::variant` and non-blittable optional alternatives still require explicit custom lowering; string, vector, span input/return, optional, unique_ptr, and shared_ptr defaults are verified.
- Ownership and allocator semantics cannot be inferred reliably from pointer syntax alone.
- Typed C variadic variants currently require `DllImport` and explicit promoted argument types.
- The four real-library gates compile generated C# and check deterministic API snapshots, but their upstream native DLLs are not present in the fixture checkout, so direct runtime calls into those four DLLs are not yet a passing gate.
- `BGCS.CppAst` still contains nullable and deprecated-API warnings that are being removed; `BGCS.Core`, `BGCS.Language`, generator packages, generated consumers, and package smoke builds are clean.
