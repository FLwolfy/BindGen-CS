# Contributing to BGCS

Follow the [C# development standard](docs/csharp-development-standard.cn.md)
and the [architecture](docs/architecture.md) when changing handwritten code.

## Architecture

- Keep BGCS independent of its consumers. Tests, examples and platform evidence belong to this repository.
- Keep the eight production projects and their declared dependency directions. Core does not depend on CppAst; Intermediate owns the frozen models; Runtime remains independent.
- Separate the generator host from the native target. Target providers describe the SDK, sysroot and ABI; parser builtin headers remain separate from system headers.
- Analyze AST and lowering inputs before emission. Emitters consume frozen models.
- Use public contracts for tests, without internal access or test-only backdoors.

## Code and project files

- Use explicit imports, English public XML documentation, semantic names and the shared declaration layout.
- Keep private fields prefixed with `m_`, properties and parameters in camelCase, and types and methods in PascalCase.
- Separate formatting changes from behavior changes. Do not edit generated output or third-party sources.
- Keep solution folders purposeful and project files readable. Preserve MSBuild conditions, metadata and evaluation order during cleanup.
- Update consumers, configuration, documentation and reviewed snapshots with API changes.

## Validation

Run `bash scripts/run-full-test-matrix.sh` before submitting functional changes.
NativeAOT and WebAssembly consumers must execute their native calls and negative
checks on the declared hosts. Record untested platforms explicitly.
