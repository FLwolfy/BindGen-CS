# Products and deployment

| Product | Purpose |
| --- | --- |
| BGCS | Complete generation library and standard composition. |
| BGCS.Core | Independent neutral models and generation interfaces. |
| BGCS.Runtime | Independent support for applications using generated bindings. |
| BindGen-CS | CLI over the same generation flow. |

Internal Engine, Analysis, NativeBridge, CSharp and Infrastructure assemblies ship with the complete product. Users do not install them one by one. There is no `BGCS.Defaults`, no Contracts alias package in 4.0, and no BGCS MSBuild integration.

## Optional official implementations

| Package | Scope |
| --- | --- |
| BGCS.Extension.BrowserWasm | Emscripten inputs, Mono Wasm profile, static table/callback strategies. |
| BGCS.Extension.NativeAot | NativeAOT execution profile and existing call/thunk mechanisms. |
| BGCS.Extension.Android | NDK target and header environment resolution. |
| BGCS.Extension.AppleMobile | Device/simulator target and SDK inputs. |
| BGCS.Extension.FreeBsd | Target and explicit sysroot inputs. |

BGCS does not depend on these packages. Install the needed implementation, then explicitly register it. Android, Apple mobile and FreeBSD target resolution does not itself supply a managed runtime or prove calls. Provide applicable runtime/ABI implementations where required. A declared capability is not a real execution receipt.

```csharp
using BGCS;
using BGCS.Extension.NativeAot;

await using BindingHost generator = BindingGenerator.CreateBuilder()
    .AddPlugin<NativeAotPlugin>()
    .Build();
```

Select its runtime ID in configuration. Package installation does not activate code through a module initializer or assembly scan. A custom implementation can live directly in your C# generation project.

## Parser resources

The complete SDK includes its locked worker managed payload and resource manifest as normal NuGet content assets. Fixed parser runtime dependencies provide native libraries. The CLI carries its own host closure. Resources follow the actual generator host, independently of the binding target.

First use verifies and prepares resources in an owned user-local directory, keyed by version, host and content identity. It does not download files, search the global NuGet cache for missing dependencies, select arbitrary system LLVM or run a compiler. An explicit deployed worker root remains available to advanced hosts.

An ordinary application build/publish output, moved to a clean location, must contain enough deployed resources to generate. Special application publishing forms need explicit resource deployment and are not assumed supported automatically.

`BGCS.ParserRuntime.osx-x64` is a technical resource package for running the parser
on an Intel Mac. The locked upstream package set supplies the other five host
resource closures; this package supplies the missing Intel macOS closure with
its actual build provenance and licenses. It is not a binding-target SDK or an
additional generation feature, and applications that only run generated bindings
do not need it. `distribution/` defines these BGCS product packages; `eng/` holds
repository-only verification and packaging tools.

## Runtime

Applications normally reference BGCS.Runtime only. It has no Core, Clang, Roslyn, MSBuild or generator dependencies. `runtime.mode = "source"` emits the required source closure in an API-local namespace; single-file output can include it. Both forms have the same lifecycle contracts.

## Repository package verification

`eng/pack.py` builds candidate products; `eng/verify-packages.py` checks exact archives. Candidate versions contain source commit and digest. Clean consumers use package bytes, not workspace DLLs. Product gates verify closures, licenses, parser provenance, optional extension activation, Runtime-only use and relocation.

Repository packaging scripts publish BGCS itself. They are not consumer packaging APIs. See [acceptance](acceptance.md) and [publishing](publish.md).
