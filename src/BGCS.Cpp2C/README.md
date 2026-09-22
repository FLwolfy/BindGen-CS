# BGCS.Cpp2C

`BGCS.Cpp2C` generates an `extern "C"` ABI bridge for selected C++ classes, functions, explicit template instances, ownership types, and STL adapters. The resulting C surface can be consumed by P/Invoke and can optionally be passed directly to BGCS to generate matching C# bindings.

This is a semantics-aware bridge generator, not an arbitrary C++ ABI translator. Unsupported or ambiguous constructs are diagnosed instead of being silently treated as blittable values.

## Install

```bash
dotnet add package BGCS.Cpp2C
```

Command-line users can install the `BindGen-CS` .NET tool and run `bindgen-cs bridge bridge.json`; the tool already includes this package.

## Generate from configuration

```json
{
  "ConfigVersion": 1,
  "EntryFiles": ["include/library.hpp"],
  "AllowedHeaders": ["include/library.hpp"],
  "OutputPath": "GeneratedBridge",
  "LanguageStandard": "c++23",
  "GenerateBuildManifest": true,
  "GenerateCSharpBindings": true,
  "CSharpNamespace": "Example.Native",
  "CSharpApiName": "NativeApi",
  "NativeLibraryName": "example",
  "CSharpOutputPath": "GeneratedBindings"
}
```

```csharp
using BGCS.Cpp2C;

Cpp2CGeneratorConfig config = Cpp2CGeneratorConfig.Load("bridge.json");
Cpp2CCodeGenerator generator = new(config);
generator.GenerateConfigured();

if (generator.LastResult is { Success: false } result)
{
    foreach (var diagnostic in result.Diagnostics)
        Console.Error.WriteLine(diagnostic);
}
```

Configuration paths are resolved relative to `bridge.json`. The bridge output contains C-facing headers, `src/Classes.cpp`, and a deterministic `bridge.manifest.json` with target, source, include, definition, compiler/linker, language-standard, and library inputs. The `BindGen-CS` tool can compile this manifest with `native-build`; embedded consumers can use `ClangNativeBuildProvider` and `NativeBuildExecutor` directly.

## Verified bridge scope

The regression suite covers constructors/destructors, instance and static methods, overloads, namespace functions, exception boundaries, pointer-adjusting multiple inheritance, explicit class and function template instances, `std::string`, `std::vector`, `std::span`, blittable and non-blittable `std::optional`, `std::unique_ptr`, `std::shared_ptr`, and configured pure-virtual callback proxies.

Unknown template specializations, arbitrary `std::variant`, and ownership or allocator contracts that configuration cannot prove remain explicit boundaries.

## Documentation

- [C++ quick start](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/getting-started.md#c-bridge)
- [Capabilities and boundaries](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/capabilities.md#c-bridge)
- [C++ configuration catalog](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/cpp2c.config.md)
- [Diagnostics](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/diagnostics.md)

BindGen-CS is licensed under the MIT License. Portions derived from CppAst/HexaGen retain their original notices.
