<h1 align="center">BindGen-CS</h1>
<p align="center">C and C++ libraries. Clear C# bindings.</p>
<p align="center">
  <a href="https://www.nuget.org/packages/BGCS"><img alt="NuGet" src="https://img.shields.io/nuget/v/BGCS?style=flat-square"></a>
  <a href="https://github.com/FLwolfy/BindGen-CS/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/FLwolfy/BindGen-CS/actions/workflows/ci.yml/badge.svg"></a>
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square"></a>
</p>
<p align="center"><a href="README.cn.md">简体中文</a> · <a href="#quick-start">Quick start</a> · <a href="#use-from-c">C# API</a> · <a href="docs/README.md">Documentation</a></p>

**BGCS generates C# bindings from C/C++ headers.** Use a terminal command or call
it from your own C# tool. The output is source code, manifests and diagnostics.

```mermaid
flowchart LR
    H["Headers + target + semantic rules"] --> G["BGCS: parse, analyze, generate"]
    G --> S["C# + necessary C/C++ bridge + manifests"]
    S --> P["Your project: compile, link, package, deploy"]
```

BGCS does not compile or run your project. When a native bridge is needed, your
project compiles that source alongside, or against, your native library.

## Quick start

Requires **.NET 10 / C# 14**. Defaults are C17 or C++23, an instance API,
FunctionTable imports and separate files.

### 1. Describe the input

```bash
dotnet tool install --global BindGen-CS --version 4.1.1
bindgen-cs init native.h --language c
```

For a header containing `int add(int left, int right);`, a minimal `bindgen.json` is:

```json
{
  "schemaVersion": 2,
  "library": { "id": "sample", "nativeName": "sample" },
  "source": { "language": "c", "headers": ["native.h"] },
  "api": { "namespace": "Sample.Generated", "name": "NativeApi", "names": { "add": "Add" } }
}
```

Paths are relative to the file that declares them. The default target is the
supported local desktop environment. Cross-target generation needs explicit target inputs.

### 2. Generate

```bash
bindgen-cs generate bindgen.json
```

```text
Generated/
├─ generation.report.json
└─ <target>/
   ├─ managed/                 C# binding source
   ├─ native/include/          Bridge headers, when needed
   ├─ native/src/              Bridge source, when needed
   └─ metadata/                ABI, native source and capability manifests
```

C++ uses the same command with `source.language` set to `cpp`. BGCS plans a C ABI
bridge automatically. By-value aggregate boundaries normally use typed pointer and
output channels; a direct import requires an applicable ABI plan.

### 3. Use the generated files

Add `managed/` sources to your .NET project, enable `AllowUnsafeBlocks`, and reference:

```xml
<PackageReference Include="BGCS.Runtime" Version="4.1.1" />
```

After your project builds the native module:

```csharp
using System;
using Sample.Generated;

using NativeApi api = NativeApi.Load(args[0]); // Path to your native module.
Console.WriteLine(api.Add(19, 23));
```

`api.Raw` exposes the physical native boundary. Friendly methods are generated
when ownership, encoding and lifetime contracts are sufficient. They never infer a
safe lifetime from a function name alone.

## Use from C#

Reference `BGCS` in your generation tool:

```csharp
using System;
using BGCS;

await using BindingHost generator = BindingGenerator.CreateDefault();
GenerationResult result = await generator.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"));

if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

This ordinary library is the **BGCS SDK**. `BindingHost` coordinates generation;
it is not a server. The full package supplies matching Clang parser resources and
prepares its isolated worker locally. It does not download a target SDK.

## Choose what you need

| Product | Purpose |
| --- | --- |
| `BindGen-CS` | CLI for terminals and scripts. |
| `BGCS` | Complete generation library, including standard implementations. |
| `BGCS.Core` | Neutral models and interfaces for your own generation extensions. |
| `BGCS.Runtime` | Module, handle, memory, string and callback support used by generated code. |
| `BGCS.Extension.*` | Optional target or execution implementations, explicitly registered. |

Your application needs generated source, Runtime support and its native module.
It does not need the generator or Clang. Set `runtime.mode` to `source` to emit
Runtime source instead of referencing the Runtime assembly.

## Extend with ordinary code

Implement an interface in your own generation project and register its factory:

```csharp
using BGCS;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;

await using BindingHost generator = BindingGenerator.CreateBuilder()
    .RegisterFactory<INativeModuleTransform>(
        new ExtensionDescriptor("sample.rules", ExtensionPort.NativeModuleTransform),
        _ => new SampleRules())
    .Build();
```

`SampleRules` implements `INativeModuleTransform`. No published plugin, manifest,
assembly scan or Clang AST is required. Other interfaces cover targets, runtime
profiles, call ABIs, lowering, API projection and artifact processing. Extensions
that cannot describe all of their inputs disable affected cache reuse.

See [extension interfaces](docs/extensibility.md) and [examples](examples/README.md).

## Platforms and SDKs

| Question | Source of the answer |
| --- | --- |
| Where does the generator run? | Host services select matching parser resources. |
| Which native platform is described? | Your target settings and target provider. |
| Where are system declarations? | Your target SDK/sysroot and header search paths. |
| What are the layouts? | The frontend interprets those inputs for the selected target. |
| How can C# call them? | A runtime profile and call ABI provider. |

A Windows parser can analyze another target's headers; that does not execute a
target program or establish runtime support. A private SDK can be supplied through
configuration and interface implementations without becoming a BGCS dependency.
An unknown ABI or unsupported runtime fails explicitly.

Optional official implementations cover Browser Wasm, NativeAOT, Android,
Apple mobile and FreeBSD. Target/SDK resolution, source generation and real
execution are separate capabilities. See [the capability boundaries](docs/capabilities.md).

## More control

| Generate | Customize | Understand |
| --- | --- | --- |
| [Getting started](docs/getting-started.md) | [Configuration](docs/configuration-guide.md) | [Architecture](docs/architecture.md) |
| [C# API](docs/api.md) | [Native bridges](docs/native-bridge.md) | [ABI safety](docs/abi-safety.md) |
| [Packages](docs/packages.md) | [Source processing](docs/source-processing.md) | [Runtime lifetimes](docs/runtime-lifetime.md) |

Three import modes: **FunctionTable**, **DllImport**, **LibraryImport**.
Instance or explicit static APIs. Split or single-file output. Local workspaces,
inspection and diffs. Supported C++ objects, templates and standard-library values
can be adapted through lowering, recipes or native shims.

[Documentation](docs/README.md) · [Testing](docs/testing.md) · [Contributing](CONTRIBUTING.md)

## License

[MIT](LICENSE). Applicable third-party copyrights and licenses are retained in
[THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md) and distributed resource notices.
