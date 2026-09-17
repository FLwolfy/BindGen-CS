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

The seven-layer architecture, shared IR, C#/Runtime/CBridge emitters, CLI workflow, transactional output, SingleFile generation, package closure, real-library regeneration budgets, and deterministic API snapshots are available now. Advanced STL/smart-pointer lowering, managed virtual callback proxies, ownership annotations, and direct invocation of the four upstream native DLLs remain in progress until their gates pass.
