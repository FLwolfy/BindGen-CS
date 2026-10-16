# Getting Started

[Wiki](README.md) | [中文](getting-started.cn.md)

## Requirements and platform evidence

Use the .NET 10 SDK. The generator, Runtime, tool and default consumers target
`net10.0`; C# 14 is the source baseline. The full SDK and tool deploy a matched
Clang worker and its native parser assets. Parsing runs in that separate process.
The system SDK, C/C++ headers and native compiler for your target remain necessary;
BGCS does not download them implicitly.

The parser assets follow the **generator host**, while headers, sysroot, layout and
native build follow the **requested target**. For example, Windows x64 can use its
Windows worker to parse an Android ARM64 target with the Android NDK. Clang parsing
can precede an MSVC or GNU native build when their target and SDK facts agree.
Selecting MSVC additionally requires its installed compiler for the initial native
source view, including when native building is disabled. `doctor` describes the
`/Zc:preprocessor`, `/PD` and `/C` prerequisites; generation verifies them with
actual preprocessing. See [native build](native-build.md).

Platform resolution, generation, linking and actual invocation are separate evidence
stages. Consult the [capability matrix](capabilities.md) and its current receipts before
claiming a feature is certified. A project that resolves a target has not thereby
proved all of its ABI, runtime or import combinations.

## Install or run from the repository

After the 3.0.0 release is published:

```bash
dotnet tool install --global BindGen-CS --version 3.0.0
bindgen-cs --help
```

Use `dotnet tool update --global BindGen-CS` for a later release, or a repository-local
.NET tool manifest to pin a tool version. Until publication, run the checked-out tool:

```bash
dotnet run --project src/BGCS.Tool -- version
```

Replace `bindgen-cs` below with that source command, keeping `--` before tool arguments.
Only four product packages need to be understood: the complete `BGCS` SDK,
`BGCS.Contracts` for neutral plugins, `BGCS.Runtime` for consumers and the
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
  "schemaVersion": 1,
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
`init`; 3.0 contains no legacy reader or migration wrapper.

## Output and native builds

`generate` parses, analyses and emits a complete candidate artifact set. It validates
models, safety, generated syntax and actual import contracts before publication.
For C++ it also generates and reparses the C ABI bridge. Native compilation requires
an explicit build request and declared native inputs:

```json
{
  "nativeBuild": {
    "mode": "sources",
    "provider": "auto",
    "sources": ["src/native.c"]
  }
}
```

Add that group to the configuration, then run:

```bash
bindgen-cs build bindgen.json
```

`build` validates managed source with warnings treated as errors and runs the
configured native build or prebuilt verification. `nativeBuild.mode = none` explicitly
skips native construction. Compilation, export verification and invocation are recorded
separately; inspect the report to see which validations ran. Failed candidate validation
does not replace the previously published complete output.

A host-target output has this shape; the target ID is resolved to a concrete ABI:

```text
Generated/
├─ .bgcs-artifacts.json    # complete owned artifact paths and digests
├─ generation.report.json
└─ windows-x64-msvc/
   ├─ managed/
   │  ├─ Imports/
   │  ├─ Api/
   │  └─ Types/
   ├─ native/
   │  ├─ include/          # C++ bridge, when needed
   │  ├─ src/              # C++ bridge, when needed
   │  └─ bin/              # when native build succeeded
   └─ metadata/
      ├─ abi.manifest.json
      └─ verification.json
```

Use the exact generated files recorded in the root `.bgcs-artifacts.json` ownership
manifest; `abi.manifest.json` describes ABI/projection metadata. Select one target
for a consumer rather than compiling sources for several incompatible targets together.
`diff` compares without replacing formal output: exit 0 means equal, 1 means a change,
and 2 means an invalid request or a failed comparison, including corrupted owned output.

## Consume the generated bindings

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="BGCS.Runtime" Version="3.0.0" />
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

## C++ and advanced native construction

```bash
bindgen-cs init include/widget.hpp --language cpp
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

C++ defaults to `c++23`. Classes, methods and selected templates pass through typed
lowering into a C ABI; raw mangled member symbols are not directly imported as C.
Configure `cpp.typeLowerings`, `cpp.callableLowerings`, `cpp.templateInstantiations`,
`cpp.nativeShims` or neutral plugins when your source needs explicit representation.
A native shim can retain a project-specific C ABI while using the same worker, build
and publication stages. Ownership assertions remain assertions, not verified evidence.

The native-only bridge entry and standalone manifest build are also available:

```bash
bindgen-cs bridge bindgen.json
bindgen-cs native-build Generated/<target>/native/metadata/bridge.manifest.json --dry-run --json
bindgen-cs native-build Generated/<target>/native/metadata/bridge.manifest.json
```

Providers are `auto`, `clang`, `gnu`, `msvc`, `clang-cl`, `cmake`, `meson` and `msbuild`.
Override the compiler or build-system executable with `--compiler`, `--build-tool`
and `--auxiliary-tool`. `--output` selects the final native file;
`--package-root` selects a separate verified native asset layout. These overrides
resolve from the invocation directory. Manifest-contained paths retain their declared
manifest/artifact base. Compiler execution uses argument lists and no command shell.
Dry-run reports both the requested final native artifact and the private execution plan.

A successful native build must verify required exports unless deliberately disabled
for an exploratory request. Disabling a check does not satisfy a release gate. musl,
GNU, static Wasm, Android and Apple native reference layouts remain target-specific;
consult capability evidence rather than inferring a layout from a short RID.

## Embedded SDK, plugins and MSBuild

```csharp
using System;
using System.Threading;
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"), CancellationToken.None);
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

For managed/native candidate builds use `host.BuildAsync(BuildRequest.FromFile(...), ct)`.
`InspectAsync`, `BridgeAsync`, `DiffAsync` and workspace methods use the same Host.
A session freezes configuration and extension registrations before parsing begins.
Ordinary plugins reference `BGCS.Contracts` and operate on detached facts, not Clang AST.
Their manifest locks the executable asset closure; artifact-affecting state and external
inputs must be fingerprinted for cache reuse.

[MSBuild integration](msbuild.md) is delivered inside the full SDK and enabled with
`BGCSGenerateBindings=true`. It uses a separate .NET TaskHost, generates into `obj`,
and imports exact managed/native items from the manifest. `BGCSBuildNative` controls
whether declared native inputs are also built. Design-time and outer builds skip
binding generation by default. Installing the SDK alone does not turn generation on.

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
bindgen-cs workspace build workspace.json
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
