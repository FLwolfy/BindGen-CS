# Contributing to BGCS

Follow the [C# development standard](docs/csharp-development-standard.cn.md)
and the [architecture](docs/architecture.md) when changing handwritten code.

## Architecture

- Keep BGCS independent of its consumers. Tests, examples and platform evidence belong to this repository.
- Follow the BGCS 4.0 module boundaries: Core owns neutral data and ports; Runtime has no generator dependencies; native parsing belongs to the Clang worker. Keep source ownership inside each module and enforce the final dependency matrix before release.
- Separate the generator host from the native target. Target providers describe the SDK, sysroot and ABI; parser builtin headers remain separate from system headers.
- Analyze AST and lowering inputs before emission. Emitters consume frozen models.
- Use public contracts for tests, without internal access or test-only backdoors.

## Code and project files

- Use explicit imports, English public XML documentation, semantic names and the shared declaration layout.
- Keep private fields prefixed with `m_`, properties and parameters in camelCase, and types and methods in PascalCase.
- Separate formatting changes from behavior changes. Do not edit generated output or third-party sources.
- Keep solution folders purposeful and project files readable. Preserve MSBuild conditions, metadata and evaluation order during cleanup.
- Keep project-owned embedded third-party resources and their license notices under that project's `Resources/` directory. Use a root `extern/` only for actual shared external sources.
- Group project files by compilation, package metadata, project dependencies, package dependencies, resources and package content. Common compiler settings belong in `Directory.Build.props`.
- Update consumers, configuration, documentation and reviewed snapshots with API changes.

## Validation

Run `bash scripts/run-full-test-matrix.sh` before submitting functional changes.
The Linux smoke job includes the targeted Browser Mono Wasm/AOT regression.
Other NativeAOT or browser application checks remain explicitly scoped developer
verification. Record untested platforms explicitly.

## Repository directories

`src/` contains neutral generation modules and Runtime; `extensions/` contains
replaceable standard and optional implementations. `eng/` holds repository-only
validation, dependency rules, toolchain locks and product packaging tools.
`distribution/` declares the BGCS product archives, parser resources and notices.
Neither directory is a consumer build integration. Historical development notes
are under `docs/history/`; current source invariants live in
[architecture review](docs/architecture-review.md), and release-specific execution
evidence is linked from [release history](docs/releases/README.md).
