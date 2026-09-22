# BindGen-CS Wiki

[English](README.md) | [简体中文](README.cn.md) | [Repository README](../README.md)

## First use

1. [Getting started](getting-started.md): go from a header to compilable bindings.
2. [Capabilities and boundaries](capabilities.md): verify that the target ABI/C++ feature is in scope.
3. [Configuration guide](configuration-guide.md): choose presets, targets, import modes, and marshalling policies.
4. [Diagnostics guide](diagnostics.md): resolve parser, ownership, buffer, callback, and C++ lowering issues.

## Find documentation by task

| Task | Document |
| --- | --- |
| C bindings | [Getting started](getting-started.md) |
| C++ to C bridge | [C++ section of Getting started](getting-started.md#c-bridge), [C++ options with dedicated regression tests](cpp2c.config.md) |
| Complete configuration property list | Run `bindgen-cs schema bindgen.schema.json` |
| Configuration entries with dedicated regression tests | [Generated tested-entry reference](config.md) |
| Embed the generator in C# | [C# API reference](api.md) |
| Choose a NuGet package | [NuGet packages and public APIs](packages.md) |
| Run tests and acceptance | [Testing](testing.md), [Acceptance specification](acceptance.md) |
| Publish packages | [Publishing](publish.md) |

## Design and quality

- [Architecture](architecture.md): layers, dependency rules, and compatibility migration status.
- [Acceptance specification](acceptance.md): 9.0 gates, budgets, and target isolation.
- [Capabilities and boundaries](capabilities.md): implementation, evidence, and unsupported-scope matrix.

## Current maturity

The CLI/workspace workflow, transactional output, single-file generation, target/toolchain model, shared IR, safety analysis, explicit emitter boundaries, package closure, real-library snapshots, selected STL/smart-pointer lowering, managed virtual callback proxies, ownership diagnostics, and InnoEngine's five-project generation gate are available now.

Complete acceptance currently proves only `macos-arm64-darwin`; every other target needs its own report. Legacy AST generation steps still exist as compatibility implementation, so the clean target architecture should not be read as a claim that every legacy path has already been removed.
