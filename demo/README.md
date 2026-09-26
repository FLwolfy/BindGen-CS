# BGCS Demo Workspace

The demo workspace contains two small, runnable examples. They reference the projects in this checkout through relative paths; the repository can live anywhere.

| Demo | Input | Output |
| --- | --- | --- |
| `BGCS.Demo` | C header | C# bindings, with package-based or standalone Runtime mode |
| `BGCS.Cpp2C.Demo` | C++ headers | C ABI headers and C++ implementation shims |

These examples demonstrate embedding the packages. New command-line users should start with the repository [getting-started guide](../docs/getting-started.md).

## Prerequisites

- .NET SDK 10.0 and the .NET 9 runtime;
- a working host C/C++ compiler environment for header discovery;
- commands run from the selected demo directory so its relative config paths resolve correctly.

## C to C# demo

```bash
cd demo/BGCS.Demo
dotnet run -- config.runtime-notgenerated.json Output
```

Use standalone Runtime source instead:

```bash
dotnet run -- config.runtime-generated.json Output
```

The program generates bindings from `headers/basic_c.h`, prints parser/generator diagnostics, and checks whether `Runtime.cs` matches the selected deployment mode.

- `config.runtime-notgenerated.json` emits bindings that expect a `BGCS.Runtime` package reference.
- `config.runtime-generated.json` emits bindings plus standalone `Runtime.cs`.
- `config.all-set.json` is a property-coverage showcase, not the recommended minimal starting configuration.

## C++ to C bridge demo

```bash
cd demo/BGCS.Cpp2C.Demo
dotnet run -- config.json Output
```

Generated C-facing headers are placed under `Output/include`; C++ implementation shims are placed under `Output/src`. The demo does not compile the bridge into a shared library—use the original native project's compiler flags, include directories, definitions, and linker inputs for that step.

`config.all-set.json` demonstrates non-default C++ bridge settings. It contains host-specific include paths and intentionally broad parser arguments, so adapt it before running on another machine; `config.json` is the portable demo entry point.

## Clean output

Both demo programs accept the output directory as their second argument. The output is reproducible and safe to exclude from source control.
