# BGCS.Cpp2C

`BGCS.Cpp2C` generates an `extern "C"` ABI bridge for C++ classes, functions, explicit template instances, ownership types, and supported standard-library semantics. The resulting C surface can be consumed by P/Invoke and can optionally be passed directly to BGCS to generate matching C# bindings.

Common C/C++ is automatic. Complex semantics use the final extension architecture: declarative type/callable lowerings, typed lowering plugins, or explicit project-owned C shims. Unsupported or ambiguous constructs are diagnosed instead of being silently treated as blittable values; projects may explicitly accept reviewed or unsafe lowerings through `LoweringSafetyPolicy`.

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
  "CSharpOutputPath": "GeneratedBindings",
  "LoweringSafetyPolicy": "VerifiedOnly",
  "TypeLowerings": [],
  "CallableLowerings": [],
  "NativeShims": [],
  "PluginAssemblies": []
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

The regression suite covers constructors/destructors, instance and static methods, overloads, namespace functions, exception boundaries, pointer-adjusting multiple inheritance, explicit class and function template instances, `std::string`, `std::vector`, `std::span`, `std::array`, `std::map`, `std::set`, `std::optional`, `std::variant`, `std::expected`, `std::filesystem::path`, `std::chrono`, `std::unique_ptr`, `std::shared_ptr`, and configured pure-virtual callback proxies.

Unknown template specializations and undeclared ownership or allocator contracts remain explicit boundaries. `VerifiedOnly` rejects project-supplied assumptions. `AllowUserAsserted` accepts reviewed project contracts. `AllowUnsafe` continues with an auditable `BGCS-SAFETY-LOWERING-BYPASS` warning; it transfers risk but cannot make an unrepresentable ABI valid.

## Documentation

- [C++ quick start](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/getting-started.md#c-bridge)
- [Capabilities and boundaries](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/capabilities.md#c-bridge)
- [C++ configuration catalog](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/cpp2c.config.md)
- [Final lowering architecture](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/lowering.md)
- [Diagnostics](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/diagnostics.md)

BindGen-CS is licensed under the MIT License. Portions derived from CppAst/HexaGen retain their original notices.
