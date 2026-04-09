# BGCS

`BGCS` is the core BindGen-CS package for generating C# interop bindings from C/C++ headers.

It includes configuration, parsing integration, metadata shaping, and C# code emission pipelines.

## Repository

- GitHub: https://github.com/FLwolfy/BindGen-CS

## Typical Use

1. Prepare a `CsCodeGeneratorConfig` JSON file.
2. Point BGCS to your header files.
3. Generate `Bindings.cs` (and optional runtime output).
