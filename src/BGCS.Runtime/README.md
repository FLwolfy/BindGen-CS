# BGCS.Runtime

`BGCS.Runtime` is the small runtime library consumed by BindGen-CS generated bindings. Install it in the application or library that compiles generated C# code; generator-only projects normally do not need a direct reference.

## Install

```bash
dotnet add package BGCS.Runtime
```

The package provides:

- `Pointer<T>`, `ConstPointer<T>`, `Bool8`, `Bool32`, and `Atomic<T>`;
- callback lifetime support through `NativeCallback<T>` and `NativeCallbackRegistry<TKey,TDelegate>`;
- `INativeContext`, `NativeLibraryContext`, and function-table loading;
- cross-platform native-library resolution and load interception;
- allocation, pointer, array, UTF-8, and UTF-16 helpers used by generated code;
- native-name and source-location metadata attributes.

## Runtime deployment modes

The default BindGen-CS configuration emits bindings that reference this NuGet package.

For source-only distribution, set `GenerateRuntimeSource=true`; generation then writes a guarded standalone `Runtime.cs`. Do not compile both copies of the Runtime types. If a project intentionally contains generated Runtime source and also references this package, define `BGCS_RUNTIME_EXTERNAL` so the generated copy is excluded.

`BGCS.Runtime` helps locate and invoke native code, but it does not build or distribute the native shared library. The consuming application remains responsible for placing the correct library for each runtime identifier and architecture.

## Documentation

- [Getting started](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/getting-started.md)
- [Package and public API guide](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/packages.md#bgcsruntime)
- [Runtime deployment tests](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/testing.md)

BindGen-CS is licensed under the MIT License. Portions derived from CppAst/HexaGen retain their original notices.
