# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

BindGen-CS is a production-oriented, cross-platform C/C++ to C# binding toolchain. It generates C# interop for C ABIs and can turn C++ classes, explicit template instances, and selected STL types into an ABI-stable C bridge with matching C# bindings.

> **Verified status:** the complete `macos-arm64-darwin` acceptance matrix passes, with all ten categories scoring 9.0/10.0. Windows, Linux, Android, iOS, and FreeBSD are represented in the target/ABI model, but design support is never presented as host verification without a target-specific acceptance report. BindGen-CS diagnoses semantics it cannot prove instead of guessing C++ ABI, ownership, or allocator behavior.

## Choose a workflow

| Your input | Recommended entry point | Result |
| --- | --- | --- |
| C header / C ABI | `bindgen-cs init native.h` | C# imports, types, constants, and optional friendly overloads |
| C++ classes / templates / STL | `bindgen-cs init library.hpp` | `bridge.json`, C bridge sources, and optional C# bindings |
| Multiple native libraries | `bindgen-cs workspace ...` | One workspace for validation, generation, and deterministic diff |
| Embedded build tooling | `BGCS` NuGet package | Stable facade, structured results, and shared binding IR |
| Generated-code consumer only | `BGCS.Runtime` NuGet package | Pointer, callback, native-context, and ABI runtime types |

See the [capability matrix](docs/capabilities.md) for evidence levels and explicit boundaries.

## Generate a C binding in five minutes

Requirements: .NET SDK 9.0 and a host Clang/GNU compiler driver or Windows LLVM.

```bash
dotnet tool install --global BindGen-CS

# native.h must already exist
bindgen-cs init native.h
bindgen-cs doctor
bindgen-cs validate bindgen.json
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

The default output is `Generated/Bindings.cs`. `build` compiles the generated source in a temporary consumer project with nullable analysis and warnings as errors. It validates the managed bindings; it does not replace the upstream native-library build.

`init native.h` creates an immediately runnable configuration. Before committing it, replace its absolute header/include paths with repository-relative paths resolved from the configuration file:

```json
{
  "Preset": "host-c,c-library",
  "Namespace": "MyCompany.Native",
  "ApiName": "NativeApi",
  "LibName": "native",
  "EntryFiles": ["include/native.h"],
  "IncludeFolders": ["include"],
  "OutputPath": "Generated",
  "ImportType": "DllImport"
}
```

Treat the output directory as reproducible build output. Customize naming, types, functions, marshalling, and ownership in configuration rather than editing generated files.

## C++ bridge

```bash
bindgen-cs init include/library.hpp
bindgen-cs bridge bridge.json
```

This emits a C ABI wrapper; the default `init` configuration also generates C# bindings from the bridge header. You still compile the generated `src/Classes.cpp` into a native shared library using the original library's compiler flags, include paths, and linker inputs.

Verified C++ coverage includes construction/destruction, instance and static methods, overloads, namespace functions, exception boundaries, multiple-inheritance pointer adjustment, explicit template instances, `std::string`, `std::vector`, `std::span`, blittable and non-blittable `std::optional`, `std::unique_ptr`, `std::shared_ptr`, and configured pure-virtual callback proxies. This is not an arbitrary C++ semantics translator; unknown specializations fail explicitly.

## CLI

| Command | Purpose |
| --- | --- |
| `init` | Create a starting configuration from a C/C++ header or config path |
| `doctor` | Inspect the host target, compiler, system includes, and macOS SDK |
| `validate` | Parse and analyze without writing final output |
| `inspect` | Print the analyzed module summary or JSON |
| `generate` | Generate bindings transactionally |
| `build` | Generate and compile-check the C# output |
| `diff` | Regenerate in temporary storage and compare checked-in bindings |
| `workspace` | Batch `validate`, `generate`, or `diff` multiple projects |
| `schema` | Generate complete JSON Schema from the installed version |
| `bridge` | Generate a configuration-driven C++ to C bridge |

See [Getting started](docs/getting-started.md) for complete examples and the [Configuration guide](docs/configuration-guide.md) for configuration decisions.

## Core capabilities

- Explicit platform, architecture, ABI, target-triple, sysroot, and compiler discovery.
- `DllImport`, `LibraryImport`, and explicit function-table/native-context import modes.
- Structs, unions, packing, bitfields, fixed arrays, typedefs, opaque handles, callbacks, and target-dependent primitives.
- `MarshallingMappings` for string encoding, ownership, cleanup, pointer/count, capacity/written-count, and caller allocation.
- The pipeline produces a shared Binding IR for safety analysis. Runtime/C Bridge use explicit emitter boundaries, while primary C# output still runs through compatibility `GenerationStep` implementations encapsulated by `CSharpEmitter`. Output replacement is transactional.
- BaseConfig composition, presets, single-file output, workspaces, deterministic diff, and target-specific snapshots.
- Separate CLI, generator, C++ bridge, Runtime, and dependency-free IR packages.

## Safety contract

BindGen-CS separates native declarations into three groups:

1. ABI and lifetime are sufficiently defined: generate and compile-check automatically.
2. Ownership, allocator, length, or callback lifetime is missing: emit an actionable `BGCS-SAFETY-*` diagnostic with the minimum configuration path.
3. A C++ type cannot be lowered safely: reject it with `BGCSCPP001` or `BGCSCPP-INSTANTIATION`.

That boundary is a correctness feature, not silent feature inflation. See the [diagnostics guide](docs/diagnostics.md).

## InnoEngine production proof

The sibling InnoEngine integration is not a toy-header demo. Its complete gate:

- deterministically regenerates cimgui, cimguizmo, miniaudio, SDL3, and bgfx from configuration;
- rejects hand-authored native imports outside `Generated/`;
- builds every required native dependency from pinned source;
- builds the complete InnoEngine solution with warnings as errors;
- runs every native binding test project.

The real-library matrix also generates, compiles, and snapshots miniaudio, SDL3, cimgui, cimguizmo, bgfx C99, and the bimg C++ bridge.

## Architecture and embedding

```text
CLI / Embedded Facade
        ↓
Configuration → Parsing → Analysis → Binding IR
                                      ↓
                  C# / Runtime / C Bridge Emitters
                                      ↓
                         Transactional Output
```

Compatibility entry point:

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
if (!generator.GenerateConfigured())
{
    foreach (var diagnostic in generator.Messages)
        Console.Error.WriteLine(diagnostic);
}
```

Use `BGCS.Facade.BindingGenerator` when you need the structured IR. See [Architecture](docs/architecture.md) for the distinction between current C# compatibility emission and the IR-native target path, plus layer ownership and migration criteria.

## Acceptance

```bash
./scripts/run-full-test-matrix.sh
```

Reports are produced only after managed tests, native ABI/runtime gates, real C/C++ libraries, deterministic snapshots, the InnoEngine workspace/native-build/test gate, and NuGet/tool smoke tests all pass:

- `artifacts/acceptance/report.json`
- `artifacts/acceptance/report.md`

See the [acceptance specification](docs/acceptance.md) for scoring and target isolation. The score proves quality within the declared scope, not complete coverage of the C++ language.

## Packages

- `BindGen-CS` — .NET tool providing the `bindgen-cs` command.
- `BGCS` — embeddable C/C++ to C# facade.
- `BGCS.Cpp2C` — C++ to C bridge generation.
- `BGCS.Runtime` — runtime used by generated bindings.
- `BGCS.Intermediate` — dependency-free Binding IR and diagnostics contracts.

See [NuGet packages and public APIs](docs/packages.md) for selection guidance.

## Documentation

- [Documentation index](docs/README.md)
- [Getting started](docs/getting-started.md)
- [Capabilities and boundaries](docs/capabilities.md)
- [Configuration guide](docs/configuration-guide.md)
- [Diagnostics guide](docs/diagnostics.md)
- [Architecture](docs/architecture.md)
- [Acceptance specification](docs/acceptance.md)
- [Testing](docs/testing.md)

## License

BindGen-CS is licensed under the MIT License. See [LICENSE](LICENSE). Portions derived from CppAst/HexaGen retain their original notices.
