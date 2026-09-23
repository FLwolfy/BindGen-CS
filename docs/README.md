# BindGen-CS Wiki

[English](README.md) | [简体中文](README.cn.md) | [Repository README](../README.md)

## First use

1. [Getting started](getting-started.md): go from a header to compilable bindings.
2. [Capabilities and boundaries](capabilities.md): verify that the target ABI/C++ feature is in scope.
3. [Compatibility and deprecation policy](compatibility-policy.md): configuration versions, public contracts, deprecation windows, and supply-chain evidence.
4. [Configuration guide](configuration-guide.md): choose presets, targets, import modes, and marshalling policies.
5. [Diagnostics guide](diagnostics.md): resolve parser, ownership, buffer, callback, and C++ lowering issues.

## Find documentation by task

| Task | Document |
| --- | --- |
| C bindings | [Getting started](getting-started.md) |
| C++ to C bridge | [C++ section of Getting started](getting-started.md#c-bridge), [C++ options with dedicated regression tests](cpp2c.config.md) |
| Complete configuration property list | Run `bindgen-cs schema bindgen.schema.json` |
| Long-term execution order and gates | [Universal execution roadmap](roadmap.md) |
| Decide whether the tool is already “super-universal” | [Engineering maturity assessment](assessment.md) |
| Configuration entries with dedicated regression tests | [Generated tested-entry reference](config.md) |
| Embed the generator in C# | [C# API reference](api.md) |
| Choose a NuGet package | [NuGet packages and public APIs](packages.md) |
| Run tests and acceptance | [Testing](testing.md), [Acceptance specification](acceptance.md) |
| Publish packages | [Publishing](publish.md) |

## Design and quality

- [Architecture](architecture.md): layers, dependency rules, and the IR-native data flow.
- [Acceptance specification](acceptance.md): 9.0 gates, budgets, and target isolation.
- [Capabilities and boundaries](capabilities.md): implementation, evidence, and unsupported-scope matrix.
- [Universal execution roadmap](roadmap.md): phased work, non-negotiable rules, and 9.0 exit criteria.
- [Engineering maturity assessment](assessment.md): quantified scores, five-workstream status, and claims that remain gated.

## Current maturity

The CLI/workspace workflow, transactional output, single-file generation, target/toolchain model, shared IR, safety analysis, package closure, real-library snapshots, declared STL/smart-pointer lowering, lifetime diagnostics, and release-governance gates are available now. InnoEngine migration is deliberately deferred until the three desktop-x64 BGCS reports pass.

The current source has a complete passing `macos-arm64-darwin` report with all ten categories at 9.0/10; Windows x64, Linux x64, and macOS x64 still require their same-version complete reports. C# output uses only the IR-native emitter, with no pre-release compatibility fallback.
