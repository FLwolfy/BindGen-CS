# BGCS.Cpp2C

`BGCS.Cpp2C` provides C++ to C bridge generation support for BindGen-CS workflows.

It helps emit C-facing adapter layers from C++ declarations so downstream interop tooling can consume a stable C API surface.

## Repository

- GitHub: https://github.com/FLwolfy/BindGen-CS

## Typical Use

1. Configure `Cpp2CGeneratorConfig`.
2. Generate bridge headers/sources from C++ headers.
3. Use the generated C API in your interop pipeline.
