# Packages and installed products

[简体中文](packages.cn.md) | [Getting started](getting-started.md) | [Publishing](publish.md)

| Product | Use |
| --- | --- |
| BGCS | Complete embedded C/C++ source generator SDK |
| BGCS.Contracts | Detached models and ordinary plugin contracts |
| BGCS.Runtime | Binding consumer support without generator/parser dependencies |
| BindGen-CS | CLI using the same `BindingHost` coordinator |

All products target .NET 10. C++ lowering and other internal modules are included in
the full SDK/tool; consumers do not install a feature package per implementation project.
The implementation has eleven product projects and no BGCS MSBuild integration assets.

## Install and generate

After the declared version has actually been published:

```bash
dotnet tool install --global BindGen-CS --version 3.0.0
bindgen-cs init include/native.h --language c
bindgen-cs doctor bindgen.json
bindgen-cs generate bindgen.json
```

Use a checkout or explicitly prepared local feed before publication. CLI and ordinary
C# SDK calls generate C#, optional C ABI bridge source, manifests and diagnostics.
The consumer owns compilation, linking, packaging and deployment; there are no product
native-build or consumer-build APIs. See [public API](api.md).

## Worker resources and SDK deployment

Parser resources follow the generator host, not the generated target. Windows→Android
uses Windows parser resources and actual Android SDK inputs. Clang builtin headers do
not substitute for target SDK/libc/C++ headers.

The SDK uses standard NuGet `contentFiles` for its locked worker/resource payload.
Local first-use provisioning keys its owned cache by version, host and manifest digest,
verifies both source and cached closure, and prepares the worker;
it does not depend on BGCS `.props`/`.targets`, a TaskHost or a NuGet-cache search at
parser execution time. Restore must deliver the complete package content. Ordinary
Contracts-only plugins do not load Clang; advanced fact contributors run in the worker.

The tool pointer and RID implementation packages deliver the declared six desktop
host closures. The technical Intel macOS parser supplement fills the upstream closure
gap. Exact products, RIDs, archive set and size limits come from
[product-packages.json](../distribution/manifests/product-packages.json), not a historic
fixed job count. Parser source/license/hash provenance remains required.

## Runtime package or source

Generated applications normally reference only `BGCS.Runtime`. With
`runtime.mode = "source"`, BGCS emits the required source closure in an API-local
namespace; single-file output can include it in `Bindings.cs`. Runtime does not
install compilers, build libraries or deploy native dependencies. Raw ABI and friendly
projection limits apply equally to package and source modes.

## Packaging and certification

The required CI plan has three generator jobs (Windows full, Linux/macOS smoke) and
one full product-install job. The install job consumes actual new package bytes in
clean private workspaces and verifies SDK, tool, Contracts plugins, Runtime consumers,
schemas and worker/resource closure. It cannot use old packages or repository project
references to satisfy installed-product proof. Core feature baselines and all original
per-host performance limits remain required.

AOT/Wasm application publication/invocation is optional manual private work, not a
mandatory installation matrix. Target facts and generated static adapters remain in
scope. Visual Studio TaskHost certification is not a product requirement.

`eng/pack.py` and `eng/verify-packages.py` implement repository packaging/verification;
consult their current `--help` before a costly local run. Partial or dirty-source
receipts remain local evidence. Final publication requires the applicable clean-revision
checks, immutable package identities and actual public availability verification.
This page does not claim 3.0.0 is already published or certified.
