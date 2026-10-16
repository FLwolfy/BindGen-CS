# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

**BGCS generates C# bindings for C and C++ libraries.** Supply native headers and a
versioned configuration. The unified Host parses declarations in an isolated Clang
worker, analyzes ABI and lifetime contracts, generates a C bridge when needed, and
emits C# with raw calls and supported friendly APIs.

BGCS 3.0 uses .NET 10 and C# 14. Its defaults are C17/C++23, an instance API,
FunctionTable imports, split output files and the independent Runtime package.
The new version reads the grouped configuration format and exposes `BindingHost`;
old configuration readers and generator APIs are not part of this interface.

## Start from a header

After version 3.0.0 has been published:

```bash
dotnet tool install --global BindGen-CS --version 3.0.0
bindgen-cs init include/native.h --language c
bindgen-cs doctor bindgen.json
bindgen-cs validate bindgen.json
bindgen-cs generate bindgen.json
```

C++ uses the same configuration and commands; select `--language cpp` and provide
the C++ header. `generate` automatically emits the supported native bridge and C#.
For source use, build the checkout and replace `bindgen-cs` with the source command:

```bash
dotnet build BindGen-CS.slnx -c Release
dotnet run --project src/BGCS.Tool -c Release -- generate examples/QuickStart/bindgen.json
dotnet run --project src/BGCS.Tool -c Release -- build examples/QuickStart/bindgen.json
```

A native compiler and the target SDK are required for native builds. The parser's
bundled builtin headers supplement the target SDK and standard library.

## Configure and build

A small C integration has one `bindgen.json`:

```json
{
  "schemaVersion": 1,
  "library": { "id": "example", "nativeName": "example" },
  "source": { "language": "c", "headers": ["include/native.h"] },
  "api": { "namespace": "Example.Bindings", "name": "NativeApi" },
  "nativeBuild": { "mode": "sources", "sources": ["src/native.c"] },
  "output": { "root": "Generated", "layout": "split" }
}
```

Paths in configuration are relative to its declaring document. `init` writes a
matching schema reference. `generate` produces managed sources, optional bridge
sources, manifests and reports under `Generated/<resolved-target>/`.

`build` compiles the candidate C# with warnings as errors and builds or verifies the
native inputs declared by `nativeBuild`. It validates required exports before
publishing the complete artifact set. A failed validation preserves the previous
owned output. With `nativeBuild.mode = "none"`, the report explicitly records that
native building was not requested. Actual invocation is a separate validation stage.

A generated consumer targets `net10.0`, enables unsafe blocks and normally references
`BGCS.Runtime`. Compile the selected target's `managed` files and deploy its verified
native library. The QuickStart example maps `bgcs_add` to `Add`:

```csharp
using QuickStart.Generated;

using NativeApi api = NativeApi.Load("/absolute/path/to/the/built/native/library");
int answer = api.Raw.Add(19, 23);
```

An API instance owns its module session and function table. Resources retain the
module leases required for cleanup. `runtime.mode = "source"` emits the required
Runtime source closure; `output.layout = "singleFile"` combines managed output into
`Bindings.cs`. See [Getting started](docs/getting-started.md) for complete consumers,
C++ construction, custom loading, runtime modes and build configuration.

## Four products, one pipeline

| Product | Purpose |
| --- | --- |
| `BGCS` | Complete embedded SDK, worker deployment and opt-in MSBuild integration |
| `BGCS.Contracts` | Neutral immutable models and ordinary plugin contracts |
| `BGCS.Runtime` | Generated binding consumer support, independent of the generator/parser |
| `BindGen-CS` | Command-line tool exposing the same Host pipeline |

Internal projects are bundled implementation modules. Users do not assemble extra
feature packages for C++, lowering or native builds. Tool RID shards and parser
runtime packages supply the six declared desktop host resource closures.

```csharp
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"));
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

MSBuild generation requires explicit `BGCSGenerateBindings = true`; generated
compilation items come from the verified manifest under `obj`. See
[MSBuild](docs/msbuild.md) and [Packages](docs/packages.md).

## ABI safety, extension and platforms

FunctionTable is the default import mode; `api.importMode` can select `dllImport` or
`libraryImport` where the runtime and ABI support them. All modes use the same native
facts and semantic analysis. Raw ABI calls remain accessible; friendly strings,
buffers, owned objects and callbacks require their explicit conversion and lifetime
contracts. Size equality alone does not prove field layout, alignment or by-value
call compatibility. Unsupported ABI forms are rejected or require a native shim.

Ordinary plugins reference `BGCS.Contracts` and implement neutral ports for targets,
transforms, lowering, projection, output processing, building or validation.
Grouped `cpp` recipes, explicit template selections and native shims handle
library-specific C++ semantics. See [Extensibility](docs/extensibility.md),
[Configuration](docs/configuration-guide.md) and the
[C++ cookbook](docs/cpp-extension-cookbook.md).

The generator host, native target, toolchain and managed runtime are separate.
Target providers supply ABI/SDK facts; browser Wasm uses static native references,
verified adapters and runtime capabilities. Adding a target family does not require
copying every emitter. Native binaries and real platform invocation still require
independent certification. Consult [Capabilities](docs/capabilities.md) and current
receipts: resolving or generating a target is not a production support claim.

## Commands and validation

| Command | Purpose |
| --- | --- |
| `init`, `schema`, `explain` | Create grouped configuration, export schema and explain diagnostics |
| `doctor`, `validate`, `inspect` | Check prerequisites/configuration and inspect frozen models |
| `generate`, `build` | Generate artifacts or validate declared managed/native builds |
| `bridge`, `native-build` | Request native-only bridge output or build a verified manifest |
| `diff`, `workspace` | Compare without publication or process explicit project dependencies |
| `supply-chain`, `version` | Inspect delivery provenance and implementation identities |

```bash
dotnet test BindGen-CS.slnx -c Release
python scripts/test-native-aot-bindings.py
python scripts/test-wasm-bindings.py
python scripts/test-wasm-bindings.py --aot
bash scripts/run-full-test-matrix.sh
```

Browser tests need the .NET 10 `wasm-tools` workload, Python and an accepted browser.
Release gates also cover real library corpora, package installation, architecture,
publication failures, worker crashes, lifetimes and performance. The full package
installer runs actual consumer applications and compares repeated package content.
Missing prerequisites fail required gates. A local partial check does not certify
all platforms or make a release ready.

- [Documentation index](docs/README.md)
- [Testing](docs/testing.md)
- [Architecture](docs/architecture.md)
- [Publishing](docs/publish.md)
- [Contributing](CONTRIBUTING.md)

## License and parser resources

BGCS uses the [MIT License](LICENSE); derived components retain their notices.
The [frontend resource directory](src/BGCS.Frontend.Clang/Resources/Clang/README.md)
records the bundled Clang builtin header source, checksum and
Apache-2.0 WITH LLVM-exception license. These resources travel with the matched
parser worker and do not replace Windows SDKs, libc, C++ libraries or target sysroots.
