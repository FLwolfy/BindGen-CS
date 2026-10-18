# Packages and distribution

[中文](packages.cn.md) | [Documentation](README.md)

BGCS 3.0 exposes four products. Project boundaries do not create additional products that users must assemble.

| Use | Product | Contents |
| --- | --- | --- |
| Embedded generation or optional build integration | `BGCS` | Host, grouped configuration, analysis, C++ bridge, C# backend, native build, isolated Clang worker, schemas and MSBuild TaskHost assets |
| Neutral plugins and model tools | `BGCS.Contracts` | Immutable native/bridge/interop/managed models and extension ports; no generator, parser or Runtime dependency |
| Generated binding consumers | `BGCS.Runtime` | Module leases, function tables, owned handles, callbacks, buffers, codecs and allocation support; no generator/parser dependency |
| Command line | `BindGen-CS` | The unified Host pipeline exposed through `bindgen-cs` |

All products require .NET 10. Internal assemblies such as `BGCS.CSharp` and `BGCS.Cpp2C` are included in the SDK rather than published as separately required feature packages. The SDK package is assembled by `distribution/BGCS.Sdk.Package`; implementation project `src/BGCS` is not independently packed.

## Install and generate

After version 3.0.0 has actually been published (until then, use the source or local-package instructions below):

```bash
dotnet tool install --global BindGen-CS --version 3.0.0
bindgen-cs init include/native.h --language c
bindgen-cs doctor bindgen.json
bindgen-cs generate bindgen.json
bindgen-cs generate bindgen.json
```

C++ uses the same grouped configuration and `generate` command. `bridge` requests only native bridge artifacts through the Host, so it does not require a managed representation of every selected ABI.

```csharp
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"));
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

`BGCS.Contracts` is sufficient for ordinary plugins. Clang-specific fact contributors belong to the worker and require the advanced frontend reference surface provided by the SDK.

## Physical packages and native resources

The four products are delivered through additional technical packages where necessary:

- The tool has a pointer package plus `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` and `osx-arm64` implementation packages.
- Upstream LLVM 20 parser runtime packages provide five host closures.
- `BGCS.ParserRuntime.osx-x64` supplies the missing Intel macOS parser closure, including its dependent libraries and licenses.

Parser resources are selected by the generator host, independently of the target being generated. A Windows generator targeting Android still uses the Windows parser closure and Android target SDK. Parser deployment never substitutes builtin Clang headers for the target platform's system SDK or C++ standard library.

The SDK contains managed worker files, per-host digest manifests and standalone TaskHost dependencies. Its build integration gathers the restored native package assets, checks the exact manifest and deploys a complete worker directory to the application. An already deployed SDK host does not need the NuGet global cache to run its parser.

The package collector unions the public SDK modules' restored graphs explicitly, so removing the Host-to-frontend project reference cannot remove the advanced Clang extension's compile dependencies. Worker and TaskHost execution closures remain separate. Their complete license inventories are collected independently of the Host graph. `BGCS`, `BGCS.Contracts` and `BGCS.Runtime` each require their declared `.snupkg`; duplicate archive entries or missing symbols fail verification.

`distribution/manifests/native-parser-assets.lock.json` pins the five upstream package identities and the actual native file hashes and CPU architectures. It also pins the Intel supplement's ClangSharp source commit and LLVM20.1.8 source archive. The Intel bootstrap records the relocated, signed dylib bytes, licenses and actual Homebrew recipe in `runtime-provenance.json`; packaging verifies the complete file set and current BGCS revision. This provenance records build inputs and bytes. The separate Intel invocation gate must pass before CI seals and distributes that artifact. An absent Intel build is a blocking condition, and an ARM64 library cannot satisfy it.

## Runtime package or source

Generated consumers normally reference only `BGCS.Runtime`. With `runtime.mode = "source"`, the generator emits the required source closure in an API-local namespace; single-file output can combine that closure into `Bindings.cs`. Source and package modes are verified with native invocation consumers. A Runtime-only package graph must not contain Clang or the generator SDK.

## Packaging and certification

`eng/pack.py` builds and collects execution closures from actual `.deps.json` files and restored project graphs. It emits the product set and verifies each physical package against the 250 MiB limit. Full packaging requires explicitly supplied, accepted Intel macOS parser assets:

```bash
python3 eng/pack.py --parser-osx-x64 artifacts/clang-runtime/osx-x64 --verify-repeat-content
python3 eng/verify-packages.py --version 3.0.0
```

`--host-only <rid>` and verifier `--sdk-only` are local partial checks. They are never complete product certification. Full installed-product certification runs on all three launch desktop hosts and a dedicated Visual Studio2026 runner after the same-revision CI matrix succeeds.

The Visual Studio gate uses the [official `windows-2025-vs2026` runner image](https://github.com/actions/runner-images), discovers the actual VS2026 `MSBuild.exe`, requires major18 and invokes its .NET TaskHost through a native build/call/clean/rebuild consumer. `dotnet msbuild` evidence is recorded separately; it does not substitute for that host.

The installer gate checks embedded C/C++ generation, Contracts-only plugins, actual native calls from Runtime-only/source/single-file consumers, NuGet native RID selection, MSBuild opt-in/off, owned clean/rebuild, separate TFM inner builds, a static project graph and installed CLI commands. The same hashed source-contributor manifest runs through CLI and MSBuild, with generated native headers/source and managed helpers checked by actual calls. Complete certification also publishes and invokes the package/source/single-file consumers with NativeAOT, then runs the complete 113-check ABI, aggregate, C++ and lifetime matrix with the installed tool and Runtime. Browser interpreter and AOT each require the complete 74-call matrix plus a separate 33-check nonblocking retained/async lifecycle subreport across all three import modes, including the generated static adapter. The installer verifies that the invocation and lifecycle subreport agree. Every matrix records its exact receipt hash and must use the same pinned installed generator, complete execution closure and Runtime bytes. These checks require .NET 10 `wasm-tools` and Chromium. CI installs the selected SDK's bundled workload manifests without updating them, and receipts record the SDK and actual workload set.

The installed-product runner also builds an advanced Clang fact plugin using only the delivered `BGCS` package, then uses its same hashed worker-only manifest in SDK, CLI and MSBuild requests. The plugin reads a live cursor, contributes detached documentation and checks that the live context expires after invocation. The SDK host must not load that plugin assembly. Generated consumers compile against Runtime alone. Adding this gate does not certify it until an actual freshly packaged run passes.

For a local partial run, `--native-aot` enables the three Add smoke publishes. With `--sdk-only --host-tool`, `--native-feature-matrix` and `--wasm` explicitly enable their complete invocation matrices. A partial product set remains partial after successful calls.

`--verify-repeat-content` repacks the same compiled binaries and compares every archive entry name and uncompressed byte sequence, including metadata and symbols. ZIP container timestamps may differ; the receipt records both archive hashes and the matching payload digest. Temporary consumers and package staging directories are removed when each run ends; verification reports and logs are retained.

Package and installed-product receipts record the checked-out source content digest as well as the Git commit and dirty state. Complete certification requires a clean tree, matching source bytes and unchanged package archives. Editing an already dirty file still changes the digest; a commit identifier alone cannot certify those local bytes. The provenance regression gate exercises real temporary Git trees and rejects changed source digests, changes during packaging and modified archive bytes.

Installed-product restores use a private NuGet packages directory and HTTP cache inside the disposable consumer workspace. Unique local package versions therefore do not accumulate in the user's permanent global cache, and an earlier installed BGCS package cannot satisfy a fresh product restore. `BGCS_MINIMUM_FREE_BYTES` can impose a stricter positive free-space floor before starting verification.

On a failed installed command, the verifier preserves bounded text reproduction inputs before cleaning consumers: at most 2 MiB per file and 8 MiB total, with omitted files listed and binary/cache trees excluded. Timeout or cancellation terminates the owned process group/tree, retaining the command log before cleanup.

The packaging receipt records the observed source revision, dirty-tree status, build configuration, use of prebuilt outputs and the exact delivered execution-asset hashes. Installed-product checks verify the complete physical package/symbol set against that receipt before restore. Complete certification requires clean packaging from the current revision; a dirty local snapshot remains explicitly partial. These records identify the delivered bytes and do not imply that an arbitrary stale `--no-build` output was rebuilt from the observed source.

Release preflight requires reviewed `implementationReady` source declarations and exact same-revision feature/configuration receipts, then every mandatory CI/product-installation job. Checked-in `releaseReady` remains false; only the aggregate preflight derives a release-ready result. See the [review and certification state flow](release-evidence-state-flow.md). A successful local build or partial SDK package does not satisfy those gates.
