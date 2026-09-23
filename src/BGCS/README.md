# BGCS

`BGCS` is the embeddable BindGen-CS generator package. Use it when a build tool, source pipeline, or application needs to generate C# interop from C headers without shelling out to the `bindgen-cs` command.

For ordinary command-line use, install the [`BindGen-CS` .NET tool](https://www.nuget.org/packages/BindGen-CS) instead. For C++ classes and templates, add [`BGCS.Cpp2C`](https://www.nuget.org/packages/BGCS.Cpp2C) and generate a C ABI bridge first.

## Install

```bash
dotnet add package BGCS
```

Projects that compile the generated bindings also need `BGCS.Runtime`, unless the configuration enables standalone runtime-source generation.

## Generate from configuration

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");

if (!generator.GenerateConfigured())
{
    foreach (var diagnostic in generator.Messages)
        Console.Error.WriteLine(diagnostic);

    throw new InvalidOperationException("Binding generation failed.");
}

var module = generator.LastResult?.Module;
```

Paths in `bindgen.json` are resolved relative to the configuration file. Successful generation replaces the configured output transactionally, so custom code should live outside the generated directory.

## What this package provides

- configuration loading, validation, presets, and target discovery;
- Clang-based C/C++ parsing and declaration analysis;
- a shared Binding IR with ownership and marshalling diagnostics;
- C# imports, native types, callbacks, constants, friendly overloads, patching, and optional standalone Runtime output;
- `DllImport`, `LibraryImport`, and function-table/native-context import modes;
- extension points for generation steps, function rules, parameter writers, patches, and IR emitters.

The primary configuration-driven C# output uses the IR-only `CSharpEmitter`. The pre-release compatibility emitter and old configuration migration path have been removed.

## Start here

- [Getting started](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/getting-started.md)
- [Configuration guide](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/configuration-guide.md)
- [Capabilities and boundaries](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/capabilities.md)
- [Public API guide](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/api.md)
- [Architecture](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/architecture.md)

BindGen-CS is licensed under the MIT License. Portions derived from CppAst/HexaGen retain their original notices.
