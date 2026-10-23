# Getting Started

[Wiki](README.md) | [中文](getting-started.cn.md)

## Requirements and platform evidence

Use the .NET 10 SDK. The generator, Runtime, tool and default consumers target
`net10.0`; C# 14 is the source baseline. The full SDK and tool deploy a matched
Clang worker and its native parser assets. Parsing runs in that separate process.
Target SDK headers and sysroots are explicit parsing inputs; BGCS does not download them.
Generation does not execute an external compiler or build a consumer. Your project
installs and runs its compiler when compiling and linking the generated sources.

Parser assets follow the **generator host**; headers, target layout and ABI follow
the **requested target**. Windows x64 can therefore parse an Android ARM64 target
with its Windows worker and Android NDK headers. The downstream compiler must use
compatible headers, defines, packing and ABI. Compiler-specific input that Clang
cannot represent requires a suitable frontend or explicit shim.

Platform resolution, generation, linking and actual invocation are separate evidence
stages. Consult the [capability matrix](capabilities.md) and its current receipts before
claiming a feature is certified. A project that resolves a target has not thereby
proved all of its ABI, runtime or import combinations.

## Install or run from the repository

Install the released command-line tool:

```bash
dotnet tool install --global BindGen-CS --version 4.0.0
bindgen-cs --help
```

Use `dotnet tool update --global BindGen-CS` for a later release, or a repository-local
.NET tool manifest to pin a tool version. To run from a source checkout:

```bash
dotnet run --project src/BGCS.Tool -- version
```

Replace `bindgen-cs` below with that source command, keeping `--` before tool arguments.
Only four product packages need to be understood: the complete `BGCS` SDK,
`BGCS.Core` for neutral plugins, `BGCS.Runtime` for consumers and the
`BindGen-CS` tool. Internal implementation assemblies are delivered by the full SDK
or tool. RID tool shards and parser runtime packages are distribution details; see
[packages](packages.md).

## Create a C configuration

In a directory containing the public header:

```bash
bindgen-cs init native.h --language c
bindgen-cs doctor bindgen.json
bindgen-cs validate bindgen.json
bindgen-cs inspect bindgen.json --json
bindgen-cs generate bindgen.json
bindgen-cs diff bindgen.json
```

Both C and C++ use `bindgen.json`. The grouped versioned format starts with:

```json
{
  "schemaVersion": 2,
  "library": { "id": "example", "nativeName": "example" },
  "source": { "language": "c", "languageStandard": "c17", "headers": ["native.h"] },
  "api": { "namespace": "Example.Bindings", "name": "NativeApi" },
  "output": { "root": "Generated", "layout": "split" }
}
```

Defaults select the host target, instance API, FunctionTable imports and package
Runtime. `init` also writes the actual schema and a `$schema` link. Ambiguous `.h`
files accept `--language c` or `--language cpp`. To place the configuration elsewhere:

```bash
bindgen-cs init include/native.h --language c --config bindings/bindgen.json
```

Each configuration's relative paths are resolved from its own directory. A CLI
`--output` override is resolved from the invocation directory. Local `extends` inputs
retain their own path origins; ordinary lists replace inherited lists. Unknown
fields and old flat configurations are rejected. Create a new configuration with
`init`; 4.0 contains no legacy reader or migration wrapper.

## Output and consumer builds

`generate` publishes complete managed sources and any required C bridge sources. Build the native project externally and compile generated C# in your consumer; BGCS does not build projects or native libraries, package binaries or deploy them. See the [QuickStart native CMake project](../examples/QuickStart/CMakeLists.txt) and [external build guide](native-metadata.md#external-compilation).

## Consume the generated bindings

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="BGCS.Runtime" Version="4.0.0" />
</ItemGroup>
```

Compile the chosen target's managed sources and deploy its native library. For the
[QuickStart](../examples/QuickStart/README.md) configuration:

```csharp
using QuickStart.Generated;

using NativeApi api = NativeApi.Load("/absolute/path/to/quickstart/native/library");
int answer = api.Raw.Add(19, 23);
```

`Raw` exposes the checked native ABI. Friendly wrappers appear when their string,
buffer and lifecycle semantics are sufficiently declared. API instances own independent
module sessions; live calls and resources retain leases until cleanup finishes.
FunctionTable resolves every required symbol before it installs a callable context.

`Load()` uses the generated context's assembly for native RID probing. A custom
module provider can be supplied to `FromProvider(...)`. With DllImport or LibraryImport,
use the generated `Create()` factory; those imports use the runtime's shared loader.
Do not assume changing the import mode proves an unsupported by-value ABI.

For standalone source delivery, choose:

```json
{
  "runtime": { "mode": "source" },
  "output": { "layout": "singleFile", "singleFileName": "Bindings.cs" }
}
```

This emits the selected Runtime closure under an API-local namespace and can compose
it into `managed/Bindings.cs`. That consumer needs no Runtime package. Use the package
or the emitted source for a given binding, avoiding duplicate implementations.

## Umbrella headers and parser options

An umbrella such as SDL3's `SDL.h` uses grouped source inputs:

```json
{
  "source": {
    "language": "c",
    "headers": ["include/SDL3/SDL.h"],
    "includeDirectories": ["include"],
    "includeTransitivelyReferencedHeaders": true,
    "includeSystemDeclarations": false
  }
}
```

`includeDirectories` is an ordered header search input. Keep output and cache roots
outside explicit include/source roots. An entry header can sit next to its configuration
without declaring `.` as an include directory. Use `parseMacros` and `parseComments`
to choose whether those facts are retained. System SDK headers are distinct from the
worker's bundled Clang builtin headers.

## C++ bridges

C++ uses the same configuration and `generate` / `bridge` entrypoints. Add emitted bridge sources and headers to your native build with the matching target ABI, SDK and compiler settings, then verify actual calls.

## Embedded SDK and plugins

```csharp
using System;
using System.Threading;
using BGCS;

await using BindingHost host = BindingGenerator.CreateDefault();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"), CancellationToken.None);
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

`InspectAsync`, `BridgeAsync`, `DiffAsync` and workspace methods use the same Host.
`Host` means coordinator, not a server or network service. A session freezes configuration and extension registrations before parsing begins.
Ordinary plugins reference `BGCS.Core` and operate on detached facts, not Clang AST.
Their manifest locks the executable asset closure; artifact-affecting state and external
inputs must be fingerprinted for cache reuse.

The SDK is an ordinary library. Its matched worker is provisioned locally on first
use from restored package content; there are no BGCS build targets. Invoke the CLI
or SDK from your own script when generation belongs in a build.

## Workspaces and customization

```json
{
  "schemaVersion": 1,
  "parallelism": 2,
  "projects": [
    { "id": "core", "configuration": "core/bindgen.json", "dependencies": [] },
    { "id": "extension", "configuration": "extension/bindgen.json", "dependencies": ["core"] }
  ]
}
```

```bash
bindgen-cs workspace validate workspace.json
bindgen-cs workspace generate workspace.json
bindgen-cs workspace diff workspace.json
```

Workspace project paths are relative to the workspace, while each binding retains its
own input base. Cycles, overlapping outputs and protected input collisions fail before
execution. A failed prerequisite blocks its dependants; independent projects can finish.

Keep handwritten partial types and plugin/recipe inputs outside owned output roots.
The publisher replaces a complete manifest-owned set and removes stale generated files.
Do not modify generated files to make a test pass. Naming belongs in `api.names`,
semantics in explicit contracts, and custom transforms in their neutral extension ports.

## Troubleshooting

- **Missing parser or compiler:** run `doctor bindgen.json`; fix the explicit worker,
  target SDK or compiler input. BGCS does not silently download a replacement.
- **No useful declarations:** inspect the retained source model, language, header search
  order and transitive/system selection.
- **Unsupported by-value type:** preserve a pointer/raw byte representation or declare a
  native shim. Equal type sizes do not prove calling-convention compatibility.
- **Missing friendly wrapper:** declare the actual encoding, bounds, retention, allocator
  and release protocol. A name such as `Create` does not prove ownership.
- **Configuration rejected:** run `schema bindgen.schema.json` and create current grouped
  inputs; plugin settings belong to their plugin namespace.
- **Unexpected cache miss:** inspect the input and extension report. An extension without
  complete fingerprints disables affected generation caching rather than returning stale files.
- **Output rejected:** use a distinct BGCS-owned output root; source, include and cache
  collisions and unowned nonempty directories are not overwritten.

Release evidence and package installation checks are described in [testing](testing.md)
and [packages](packages.md). They must come from the actual release revision.
