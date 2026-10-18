# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

**BGCS generates C# bindings for C and C++ libraries.** Its input is native source
headers plus a versioned configuration. Its output is C#, any required C ABI bridge
sources, manifests and diagnostic metadata. Your application build owns compilation,
linking, packaging and deployment.

```text
headers + config → BGCS parse / analyze / generate → C# + bridge + manifests
                                                        ↓
                             consumer compile / link / package / deploy
```

Use the CLI or call the ordinary C# library; both enter the same pipeline.
`BindingHost` means the coordinator of a generation request, not a network service
or a background daemon. Parsing runs in an isolated Clang worker. The generator is
separate from `BGCS.Runtime`, which supports calls and lifetimes in the generated app.

BGCS 3.0 uses .NET 10 / C# 14. Defaults are C17/C++23, an instance API,
FunctionTable imports, split files and package Runtime. There is no legacy generator
facade, flat configuration reader or built-in consumer/native build command.

## Generate from a header

After 3.0.0 is published:

```bash
dotnet tool install --global BindGen-CS --version 3.0.0
bindgen-cs init include/native.h --language c
bindgen-cs doctor bindgen.json
bindgen-cs validate bindgen.json
bindgen-cs generate bindgen.json
```

For a checkout, build the tool and use it directly:

```bash
dotnet build src/BGCS.Tool -c Release
dotnet run --project src/BGCS.Tool -c Release --no-build -- generate examples/QuickStart/bindgen.json
```

C++ uses the same commands with `--language cpp`. Supported C++ operations are lowered
to bridge sources automatically. Generation does not compile those sources or certify
that a downstream native library matches them.

```json
{
  "schemaVersion": 1,
  "library": { "id": "example", "nativeName": "example" },
  "source": { "language": "c", "headers": ["include/native.h"] },
  "api": { "namespace": "Example.Bindings", "name": "NativeApi" },
  "output": { "root": "Generated", "layout": "split" }
}
```

Each declaring configuration owns its relative paths. `init` also writes a schema.
Compile the chosen target's managed files with unsafe enabled and normally reference
`BGCS.Runtime`. Compile/link the bridge with your native project using compatible
headers, defines, packing and ABI. See [QuickStart](examples/QuickStart/README.md) and
[getting started](docs/getting-started.md).

```csharp
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"));
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

The SDK provisions its matched host worker from restored package content on first use;
it does not require BGCS build targets. You may run generation from your own build
script, application or tool. The generated application's ordinary build remains yours.

## Four products, eleven implementation projects

| Product | Purpose |
| --- | --- |
| `BGCS` | Complete embedded generator SDK, including built-ins and worker resources |
| `BGCS.Contracts` | Neutral models and ordinary plugin contracts |
| `BGCS.Runtime` | Independent binding consumer support |
| `BindGen-CS` | CLI for the same generation pipeline |

Internal modules are delivered by the full SDK/tool; users do not assemble extra C++
feature packages. Runtime can instead be emitted with `runtime.mode = "source"`.
`output.layout = "singleFile"` combines managed output into `Bindings.cs`.

Generated API instances own module sessions. Active calls, owned handles, callbacks
and operations retain the leases required for cleanup. Raw ABI calls remain accessible;
friendly string/Span/owned APIs require explicit semantic and lifetime evidence.

## Host, target, SDK and facts

An omitted target selects the detected host target; Linux GNU and musl are distinct.
An explicit target selects another ABI. A target provider maps that identity and
capabilities; Clang reads the declarations and observes target type/layout facts.
Neither a provider nor a target name supplies proprietary SDK headers.

For example, Windows can generate Android ARM64 bindings using its Windows parser
worker and explicitly supplied NDK sysroot/header paths. The Android compiler, linking
and device deployment belong to the consumer. See [platform extension](docs/platform-extension.cn.md).
Private plugins may describe a special SDK/target, but must provide the actual inputs,
capabilities and an existing supported managed runtime. Unknown ABI classification or
runtime behavior still requires implementation or a shim. This is not zero-config
support for Switch or any other proprietary platform.

Clang need not be the final native compiler. Compiler-specific header branches, layout
and calling conventions must still agree; unsupported input is diagnosed rather than
patched by a hidden compiler execution path. Size equality alone does not prove ABI
compatibility. Target resolution, generation and actual platform calls are different claims.

## Extension and verification

Contracts-only plugins provide target mappings, source inputs, transforms, C++ lowerings,
managed projection, backend emission, processors and validators. Services are session-owned;
state and external inputs need fingerprints for cache reuse. Parser-specific advanced
contributors run only in the worker. See [extensibility](docs/extensibility.md),
[configuration](docs/configuration-guide.md) and the [C++ cookbook](docs/cpp-extension-cookbook.md).

Required CI consists of three generator jobs (Windows full, Linux/macOS smoke) and
one full installed-product job. Core feature baselines and the original per-host
performance budgets remain required. AOT/Wasm application build/run checks are manual,
optional private diagnostics; their target facts and source generation stay in scope.
No local subset establishes final release or universal platform certification.

- [Documentation](docs/README.md) · [Architecture](docs/architecture.md) · [Assessment](docs/assessment.md)
- [Testing](docs/testing.md) · [Packages](docs/packages.md) · [Publishing](docs/publish.md)

## License and parser resources

BGCS uses the [MIT License](LICENSE); retained components keep their notices.
The [parser resources](src/BGCS.Frontend.Clang/Resources/Clang/README.md) record Clang
builtin header provenance and Apache-2.0 WITH LLVM-exception licensing. They supplement,
and do not replace, the target SDK, libc or C++ standard library.
