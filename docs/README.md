# BGCS 3.0 documentation

[简体中文](README.cn.md) | [Repository README](../README.md)

The product generates source: headers/local config → parse/analyze/generate →
C#/bridge/manifests. Consumers own compile/link/package/deploy. CLI and ordinary
C# SDK calls share `BindingHost`, the generation coordinator.

## Start

1. [Getting started](getting-started.md): generate and consume externally.
2. [Configuration](configuration-guide.md) and [exact field reference](generated/config-reference.md).
3. [Packages](packages.md): complete SDK, Contracts plugins, Runtime and CLI.
4. [Public API](api.md): ordinary library calls, sessions, inspection and workspaces.

## Design and extension

- [Architecture](architecture.md) and [assessment](assessment.md): eleven modules and actual boundaries.
- [Capabilities](capabilities.md), [ABI safety](abi-safety.md), [lifetimes](runtime-lifetime.md).
- [Plugins](extensibility.md), [C++ cookbook](cpp-extension-cookbook.md), [lowering](lowering.md).
- [Platform extension](platform-extension.cn.md) and [CustomTargetPlugin](../examples/CustomTargetPlugin/README.md).
- [Callbacks](callbacks.md), [naming](naming.md), [source processing](source-processing.md).
- [External compilation/native metadata](native-metadata.md), [diagnostics](diagnostics.md).

## Acceptance and history

[Testing](testing.md), [core acceptance](acceptance.md), [release verification](release-verification.md)
and [publishing](publish.md) distinguish required baselines, installation and publication.
Required CI is Windows full, Linux/macOS smoke and one full product-install job;
original performance budgets remain. AOT/Wasm application builds/runs are optional
manual private checks; their target facts and source generation remain in product scope.

[The roadmap](roadmap.md) records current convergence. [Historical releases](releases/README.md),
old plans and checkpoints describe their original revisions, not current APIs,
compatibility guarantees or completed certification.
