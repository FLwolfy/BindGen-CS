# BGCS 3.0 Documentation

[English](README.md) | [简体中文](README.cn.md) | [Repository README](../README.md)

BGCS 3.0 uses .NET 10, grouped versioned configuration and the public `BindingHost`
pipeline. Current implementation and acceptance status are recorded in
[implementation status](implementation-status.cn.md), the machine-readable
[feature inventory](../eng/acceptance/feature-inventory.json) and actual validation
receipts. A successful local subset does not establish release readiness.

## First integration

1. [Getting started](getting-started.md): input headers, generate bindings, declare native inputs and consume an instance API.
2. [Configuration guide](configuration-guide.md): grouped defaults, paths, semantic contracts, extensions and output modes.
3. [Generated configuration reference](generated/config-reference.md): every current schema property, constraint and default.
4. [Packages](packages.md): complete SDK, Contracts-only plugins, Runtime consumers and the tool's physical distribution.
5. [MSBuild integration](msbuild.md): explicit opt-in, exact manifest items and independent inner builds.

## Architecture and extension

| Task | Document |
| --- | --- |
| Understand module ownership, dependencies and pipeline | [Architecture](architecture.md) |
| Embed generation, building, inspection or a workspace | [Public API](api.md) |
| Build and link generated sources with your own native build system | [Native construction](native-metadata.md#external-compilation) |
| Understand resources, callback drain and asynchronous cancellation | [Runtime lifetimes](runtime-lifetime.md) |
| Configure synchronous callbacks, returned-handle registrations and terminal operations | [Callback cookbook](callbacks.md) |
| Distinguish storage, calling ABI and semantic evidence | [ABI safety](abi-safety.md) |
| Inspect original callable/type/parameter names on canonical raw signatures | [Native name attributes](native-metadata.md) |
| Implement a neutral plugin | [Extensibility](extensibility.md) |
| Understand a diagnostic | [Diagnostics](diagnostics.md) |
| Add C++ type/callable lowering or a native shim | [C++ cookbook](cpp-extension-cookbook.md), [lowering architecture](lowering.md) |
| Inspect grouped C++ configuration | [Generated C++ reference](generated/cpp-config-reference.md) |
| Understand host/target/SDK separation | [Platform extension](platform-extension.cn.md) |
| Follow design decisions | [Architecture decision records](adr/) |

## Verification and release

- [Testing](testing.md) describes required native, NativeAOT, browser Wasm and repository acceptance execution.
- [Package certification](packages.md#packaging-and-certification) installs actual local products, compiles and invokes consumers, validates schema closure and compares repeated package content.
- [Publishing](publish.md) describes same-revision release gates, product packages and actual authorized publication.
- [Baseline option dispositions](parity-option-dispositions.md) records exact old consumers and explicit managed presentation selection.
- [Contributing](../CONTRIBUTING.md) records repository development requirements.
- [Release history](releases/README.md) retains historical release availability and version records.

Platform resolution, parsing, generation, managed compilation, native linking,
invocation and package consumption are separate stages. Current receipts must show
the exact target, runtime, import mode, toolchain and source revision for each claim.
Historical reports and former maturity scores remain historical evidence; they do
not certify the 3.0 implementation or substitute for its mandatory release gates.
