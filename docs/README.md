# BindGen-CS Wiki

[English](README.md) | [简体中文](README.cn.md) | [Repository README](../README.md)

## User guides

1. [Getting started](getting-started.md)
2. [Configuration guide](configuration-guide.md)
3. [C# API reference](api.md)
4. [NuGet packages and exports](packages.md)
5. [C++ bridge configuration](cpp2c.config.md)
6. [Testing](testing.md)
7. [Publishing](publish.md)

## Design and quality

- [Architecture](architecture.md)
- [Acceptance specification](acceptance.md)

## Stable behavior versus work in progress

The seven-layer architecture, shared IR, C#/Runtime/CBridge emitters, CLI/workspace workflow, transactional output, SingleFile generation, target/toolchain modeling, package closure, real-library budgets, deterministic target-specific snapshots, STL/smart-pointer lowering, managed virtual callback proxies, ownership diagnostics, and InnoEngine's five-project generated-binding gate are available now. The generated acceptance report is the authoritative statement of what passed on a particular target.
