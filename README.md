# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

BindGen-CS is a cross-platform C/C++ to C# binding toolchain. It generates C# interop directly from C APIs, or turns C++ classes, template instances, and common STL types into a stable C ABI bridge with matching C# bindings.

## What it does

- Generates `DllImport`, `LibraryImport`, or function-table bindings from C/C++ headers.
- Emits a raw ABI API plus `string`, `Span<T>`, `ref`, and `out` overloads where the required safety semantics are known.
- Handles structs, unions, packing, bitfields, fixed arrays, typedefs, opaque handles, callbacks, and target-dependent primitives.
- Builds C bridges for C++ classes, construction/destruction, methods, overloads, inheritance, template instances, common STL containers, smart pointers, paths, and chrono values.
- Discovers compilers, target triples, sysroots, and system includes, then writes a reproducible native build manifest.
- Verifies real shared-library exports, creates multi-RID native package layouts, and tests clean NuGet consumers.
- Manages multiple native libraries through one workspace with deterministic diffs, transactional output, and incremental caching.
- Extends project-specific semantics through declarative lowerings, independent plugins, or project-owned C shims—without adding library-specific branches to BGCS core.

BGCS does not translate arbitrary C++ source line by line into C#. Its job is to expose callable native capabilities to C# reliably. For C APIs, unproven ownership, allocator, buffer, or callback semantics produce a diagnostic and suppress inferred friendly overloads by default; the raw ABI remains available. Unsupported C++ lowerings stop bridge generation until a recipe, plugin, or shim supplies the missing semantics.

## Getting started

You need .NET SDK 9.0. The commands below run directly from a source checkout and do not require a published package. C++ bridges also need a local C/C++ compiler.

### Generate C# from a C header

From the repository root, copy and run:

```bash
dotnet run --project src/BGCS.Tool -- init examples/QuickStart/native.h --config examples/QuickStart/bindgen.json
dotnet run --project src/BGCS.Tool -- generate examples/QuickStart/bindgen.json
dotnet run --project src/BGCS.Tool -- build examples/QuickStart/bindgen.json
```

The result is:

```text
examples/QuickStart/Generated/
└─ Bindings.cs
```

`init` creates a runnable configuration beside the example header. `generate` writes the bindings, and `build` compiles them in a temporary consumer project with nullable analysis and warnings as errors. Replace the example header with your own and keep its configuration next to it. After the tool is published, `dotnet tool install --global BindGen-CS` will provide the shorter `bindgen-cs` command used below; until then, replace `bindgen-cs` with `dotnet run --project src/BGCS.Tool --`.

For a first integration, run the complete check:

```bash
bindgen-cs doctor
bindgen-cs validate bindgen.json
bindgen-cs inspect bindgen.json
bindgen-cs build bindgen.json
```

### Generate a C bridge and C# from C++

```bash
bindgen-cs init path/to/library.hpp
bindgen-cs bridge bridge.json
bindgen-cs native-build GeneratedBridge/bridge.manifest.json
```

The default configuration produces:

```text
GeneratedBridge/        # C ABI headers, C++ wrappers, and build manifest
Generated/              # matching C# bindings
```

To stage the native library into a NuGet RID layout:

```bash
bindgen-cs native-build GeneratedBridge/bridge.manifest.json \
  --package-root artifacts/native-package
```

Generated directories are reproducible output; do not edit them. Put naming, type, marshalling, ownership, and function-selection rules in configuration. See [Getting started](docs/getting-started.md) for complete project layouts and troubleshooting.

## Current support status

Legend: implemented and accepted on the current version ✅　implementation or host acceptance still pending ⚠️.

| Capability | Status | Notes |
| --- | :---: | --- |
| C to C# bindings | ✅ | Raw ABI and friendly APIs share one IR-native generation path |
| C++ to C bridge to C# | ✅ | Classes, inheritance, template instances, common STL, and smart pointers have native invocation tests |
| Project extensions | ✅ | Declarative lowerings, typed plugins, C shims, and managed/native artifacts |
| Multi-RID native packages | ✅ | `runtimes/<rid>/native/` for Windows, Linux, and macOS x64/arm64 |
| SBOM, provenance, API/license/vulnerability gates | ✅ | Local generation and release gates are implemented |
| GitHub OIDC release signing | ⚠️ | The workflow is configured; a real signature requires an authorized GitHub release run |

### Platform acceptance

Every listed platform is a support target. ⚠️ means the current version does not yet have a complete independent host report; it does not mean permanently unsupported.

| Platform / architecture | Status | Current state |
| --- | :---: | --- |
| macOS arm64 | ✅ | Complete `macos-arm64-darwin` report passed |
| Windows x64 | ⚠️ | Real clang-cl, MSBuild, DLL invocation, and NuGet consumer report pending |
| Linux x64 | ⚠️ | Same-version complete host and NuGet consumer report pending |
| macOS x64 | ⚠️ | ClangSharp 20 has no upstream Intel native package; CI builds it locally, with complete Intel and portable NuGet reports pending |
| Windows arm64 | ⚠️ | Target/RID model exists; provider and runtime acceptance pending |
| Linux arm64 | ⚠️ | Target/RID model exists; independent complete report pending |
| Android | ⚠️ | NDK/sysroot, package layout, and device/emulator acceptance pending |
| iOS | ⚠️ | Xcode SDK, XCFramework layout, and device/simulator acceptance pending |
| FreeBSD | ⚠️ | Toolchain, package layout, and runtime acceptance pending |

See [Capabilities and boundaries](docs/capabilities.md) and the [Acceptance specification](docs/acceptance.md) for detailed evidence.

## Choose a workflow

| Input or scenario | Use |
| --- | --- |
| C header / C ABI | `bindgen-cs init native.h`, then `generate` or `build` |
| C++ class / template / STL | `bindgen-cs init library.hpp`, then `bridge` and `native-build` |
| Multiple native libraries | `bindgen-cs workspace validate/generate/diff` |
| Embed in existing build tooling | Reference `BGCS` or `BGCS.Cpp2C` |
| Consume generated code only | Reference `BGCS.Runtime` |

## C++ coverage and extension

Verified coverage includes construction/destruction, instance and static methods, overloads, namespace functions, exception boundaries, multiple-inheritance pointer adjustment, full and partial template specializations, explicit template instances, `string`, `vector`, `span`, `array`, `map`, `set`, `optional`, `variant`, `expected`, `filesystem::path`, `chrono`, `unique_ptr`, `shared_ptr`, and configured pure-virtual callback proxies.

There are three ways to add project-specific behavior:

1. `TypeLowerings` / `CallableLowerings` for stable conversions expressible in JSON.
2. A typed lowering plugin when matching needs AST inspection, target branches, or extra generated artifacts.
3. `NativeShims` when project C/C++ must define the stable C ABI boundary.

The [C++ extension cookbook](docs/cpp-extension-cookbook.md) contains a runnable shim, an independent plugin project, callback/async/allocator patterns, and guidance for `AllowUnsafe`.

## Common commands

| Command | Purpose |
| --- | --- |
| `init` | Create a starting configuration from a header |
| `doctor` | Inspect the host target, compiler, system includes, and SDK |
| `validate` | Parse and analyze a C binding configuration without writing final output |
| `inspect` | View the analyzed module summary or JSON |
| `generate` | Transactionally generate C# bindings |
| `build` | Generate and compile-check C# bindings |
| `diff` | Check whether committed bindings need regeneration |
| `workspace` | Process multiple configurations as one workspace |
| `bridge` | Generate a C++ to C bridge and optional C# bindings |
| `native-build` | Compile a bridge, verify exports, and optionally stage a RID package |
| `schema` | Generate strict JSON Schema from the installed version |
| `explain` | Explain stable diagnostic codes |
| `supply-chain` | Generate SPDX SBOM and SLSA provenance |

## Safety behavior

- Clear ABI and lifetime: generate and compile-check.
- Missing ownership, allocator, buffer length, or callback lifetime: emit a `BGCS-SAFETY-*` diagnostic, preserve raw ABI, and suppress unproven friendly overloads by default. Set `StrictSafetySeverity=Error` to reject the whole generation, or supply an explicit `MarshallingMappings` contract to restore the friendly API.
- No accepted C++ lowering: stop until a recipe, plugin, or shim is supplied.
- `AllowUnsafe` explicitly transfers risk and keeps an audit diagnostic; it cannot repair an invalid ABI.

## Embed the generator

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
if (!generator.GenerateConfigured())
{
    foreach (var diagnostic in generator.Messages)
        Console.Error.WriteLine(diagnostic);
}
```

Use `BGCS.Facade.BindingGenerator` and the Binding IR in `BGCS.Intermediate` when structured results are required.

## Testing and release

```bash
./scripts/run-full-test-matrix.sh
```

The full matrix covers managed tests, native ABI/runtime behavior, C++ semantics, real libraries, public APIs, deterministic snapshots, NuGet consumers, license/vulnerability policy, and performance. See the [Acceptance specification](docs/acceptance.md) for report details and [Publishing](docs/publish.md) for release and OIDC requirements.

## Packages

- `BindGen-CS` — .NET tool providing the `bindgen-cs` command.
- `BGCS` — embeddable C/C++ to C# facade.
- `BGCS.Cpp2C` — C++ to C bridge generation.
- `BGCS.Runtime` — runtime used by generated bindings.
- `BGCS.Intermediate` — Binding IR and diagnostics contracts without generator dependencies.

## Documentation

- [Documentation index](docs/README.md)
- [Getting started](docs/getting-started.md)
- [Configuration guide](docs/configuration-guide.md)
- [Capabilities and boundaries](docs/capabilities.md)
- [C++ extension cookbook](docs/cpp-extension-cookbook.md)
- [Diagnostics guide](docs/diagnostics.md)
- [Architecture](docs/architecture.md)
- [Testing and acceptance](docs/testing.md)
- [Publishing and OIDC](docs/publish.md)

## License

BindGen-CS is licensed under the MIT License. See [LICENSE](LICENSE). Portions derived from CppAst/HexaGen retain their original notices.
