# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

**BindGen-CS (BGCS) generates the code that lets C# call C and C++ libraries.**
You provide native headers and configuration; BGCS produces C# bindings. For C++ APIs,
it can also generate a C bridge that exposes classes and supported templates to C#.

## Understand the three pieces

| Piece | What it does | Who provides it |
| --- | --- | --- |
| Header (`.h` / `.hpp`) | Describes native functions and types | The native library |
| Native library (`.dll`, `.so`, `.dylib`, or Wasm-linked code) | Implements those functions | Your native build |
| C# bindings (`Bindings.cs`) | Lets C# call those functions with the correct ABI | BGCS |

Generating bindings does not compile the original library, translate its implementation
into C#, or deploy your application. A C# bindings assembly and a native DLL are different
parts of the integration.

```text
C API:    header ────────────────> C# bindings ──> calls the native library
C++ API:  header ──> C bridge ───> C# bindings ──> calls the compiled bridge
```

## Getting started

### 1. Prepare your environment

For this source checkout, install **.NET SDK 10.0** and the **.NET 9 runtime**.
The repository's `global.json` selects SDK 10; the projects target `net9.0`.
Check your installation with `dotnet --list-sdks` and `dotnet --list-runtimes`.

A native compiler and the target's SDK are needed when compiling a library or C++ bridge.
BGCS bundles its parser's Clang builtin headers; these are compiler support files,
not replacements for your target SDK or C/C++ standard library.

### 2. Generate your first bindings

Open a terminal in the repository root and run:

```bash
dotnet run --project src/BGCS.Tool -- init examples/QuickStart/native.h --config examples/QuickStart/bindgen.json
dotnet run --project src/BGCS.Tool -- generate examples/QuickStart/bindgen.json
dotnet run --project src/BGCS.Tool -- build examples/QuickStart/bindgen.json
```

- `init` creates the starting configuration. Run it once for a new integration.
- `generate` writes `examples/QuickStart/Generated/Bindings.cs`.
- `build` generates and compile-checks the C# bindings in a temporary consumer project.
  **It does not build the native library or prove that native calls work.**

The example header declares `bgcs_add`. Replace the header with your library's header
and review the configuration before using it in your project. Generated files are
reproducible output; make changes in configuration, not in `Bindings.cs`.

### 3. Use the bindings in your C# project

1. Include the generated `Bindings.cs` in your project.
2. Reference `BGCS.Runtime`. From a source checkout, add a project reference to
   `src/BGCS.Runtime/BGCS.Runtime.csproj`; a published package can use a package reference.
3. Build and deploy the native library for your application's target. `libName` in the
   configuration must match the library's loadable name, and the entry points must be exported.
4. Call the generated API and test it against the real native library.

For the default example configuration, the call looks like this **after** the native
implementation is available:

```csharp
using Native.Bindings;

int result = NativeApi.BgcsAdd(2, 3);
```

The initial configuration uses `Native.Bindings`, `NativeApi`, and library name `native`.
Change `namespace`, `apiName`, and `libName` to fit your project. The complete integration
and troubleshooting guide is in [Getting started](docs/getting-started.md).

Set `<DisableRuntimeMarshalling>true</DisableRuntimeMarshalling>` in the consumer project.
Generated wrappers already own ABI conversion; .NET should call the declared carriers directly.
This applies across targets. Run actual native calls to verify the result; compilation and matching `sizeof` values are insufficient.

## C++ libraries

A C++ class cannot generally be called through a C ABI directly. BGCS can generate the
bridge and matching bindings for its supported C++ semantics:

```bash
dotnet run --project src/BGCS.Tool -- init path/to/library.hpp
dotnet run --project src/BGCS.Tool -- bridge bridge.json
dotnet run --project src/BGCS.Tool -- native-build GeneratedBridge/bridge.manifest.json
```

`GeneratedBridge/` contains C headers, C++ wrappers, and a native build manifest;
`Generated/` contains the C# bindings. The bridge configuration must include the original
library's required sources, libraries, and include paths. `native-build` builds the bridge
from that manifest and verifies its exports.

Supported lowerings include classes, overloads, inheritance, configured template instances,
common STL containers, smart pointers, and configured callback proxies. Unknown C++ semantics
are rejected until a lowering or project-owned shim defines them. See
[Capabilities](docs/capabilities.md) and the [C++ extension cookbook](docs/cpp-extension-cookbook.md).

## Cross-platform and WebAssembly

**The machine running BGCS and the platform running your application are separate.**
Windows, Linux, and macOS can produce bindings for an explicitly selected target, provided
the relevant SDK and parser runtime are available. Native libraries still need target-specific
builds. Pointer size, `long`, packing, calling conventions, and conditional declarations may
change the ABI; a successful build on one platform does not certify another.

| Scenario | What you configure |
| --- | --- |
| Desktop native library | Target ABI, compiler/SDK paths, and library name |
| WebAssembly through Emscripten | `emscripten-c` / `emscripten-cpp` preset and the target SDK/sysroot |
| Another WebAssembly environment | Its own target and runtime contracts; WASI is not an Emscripten alias |

Emscripten support uses the same parser, analysis, IR, and emitter as desktop targets.
BGCS generates the bindings; your application toolchain compiles and links the native Wasm
code and supplies browser startup. BGCS does not depend on a game engine.

The independent [Wasm invocation test](docs/testing.md#independent-webassembly-invocation)
generates all three import modes and calls a BGCS-owned C API and generated C++ bridge inside
a real browser. Its 47 checks cover values, layouts, buffers, opaque handles, callbacks,
construction, inheritance, and release. The same fixture runs in a standalone NativeAOT consumer.
See [target evidence](docs/capabilities.md#target-evidence) for tested hosts and remaining gates;
Windows, Linux, macOS, mobile, packaging, and browser coverage have separate acceptance scopes.

## Import modes, safety, and extension

- **`DllImport`**: conventional P/Invoke declarations.
- **`LibraryImport`**: .NET source-generated P/Invoke declarations.
- **`FunctionTable`**: calls through resolved function pointers; an `INativeContext` can provide
  application-owned symbol resolution. It does not make unavailable dynamic loading available.

BGCS keeps raw ABI bindings and generates convenient `string`, `Span<T>`, `ref`, and `out`
overloads when their safety contracts are known. Missing ownership, allocator, buffer length,
or callback lifetime information produces diagnostics and suppresses unproven friendly
overloads by default. Configuration can make these diagnostics fatal.

Extend project-specific behavior with `TypeLowerings` / `CallableLowerings`, typed lowering
plugins, or native shims. Keep library-specific semantics in those extensions.
See [Configuration](docs/configuration-guide.md), [Diagnostics](docs/diagnostics.md), and
[Architecture](docs/architecture.md).

## Everyday commands

Run `dotnet run --project src/BGCS.Tool -- <command>` from this checkout.
After installing a published `BindGen-CS` tool package, use `bindgen-cs <command>` instead.

| Command | Purpose |
| --- | --- |
| `doctor` | Inspect the host compiler, includes, and SDK |
| `validate` / `inspect` | Check a configuration or inspect its analyzed API |
| `generate` / `build` | Generate C# or additionally compile-check it |
| `bridge` / `native-build` | Generate and compile a C++ bridge |
| `diff` | Check whether generated bindings need updating |
| `workspace` | Process several native libraries together |
| `schema` / `explain` | Export configuration schema or explain a diagnostic |

For build-tool integration, reference `BGCS` or `BGCS.Cpp2C`. Generated-code consumers
normally reference only `BGCS.Runtime`; `BGCS.Intermediate` contains the independent IR contracts.
See [Packages and APIs](docs/packages.md).

## Tests and documentation

```bash
dotnet test BindGen-CS.sln -c Release
python scripts/test-wasm-bindings.py
python scripts/test-wasm-bindings.py --aot
python scripts/test-native-aot-bindings.py
```

The Wasm test additionally requires a **.NET 9 SDK with `wasm-tools`**, Python 3.10+, and
Chrome, Chromium, or Edge. Use `--dotnet` and `--browser` to select installed executables.
Missing prerequisites or an incomplete native invocation fail the test; they are not skipped.

For the complete target release gates, run `bash scripts/run-full-test-matrix.sh`.
Native ABI tests, deterministic snapshots, NuGet consumers, supply-chain checks, and performance
are separate from the focused Wasm test. See [Testing](docs/testing.md) and [Acceptance](docs/acceptance.md).

- [Documentation index](docs/README.md)
- [First integration](docs/getting-started.md)
- [Configuration reference](docs/configuration-guide.md)
- [Capabilities and platform evidence](docs/capabilities.md)
- [C++ extension cookbook](docs/cpp-extension-cookbook.md)
- [Publishing](docs/publish.md)
- [Contributing](CONTRIBUTING.md)

## Why does BGCS include `extern/clang-resource`?

The archive contains Clang's builtin C/C++ headers, such as `stddef.h` and architecture intrinsics.
It is shared by Windows, Linux and macOS parser hosts and is not a macOS SDK.
The current parser embeds these headers so its libclang runtime has a matching resource directory
without requiring a separate Clang installation. Keep the archive when building or packaging BGCS.

Builtin headers do not replace a target SDK. Target providers select the target's ABI, sysroot
and system headers; an Emscripten target, for example, still needs its Emscripten SDK.
See [Clang's cross-compilation documentation](https://clang.llvm.org/docs/CrossCompilation.html)
and the [bundled resource provenance](extern/clang-resource/README.md).

## License

BGCS uses the [MIT License](LICENSE). Derived CppAst/HexaGen portions retain their notices.
Bundled Clang builtin headers use Apache-2.0 WITH LLVM-exception; their
[source, checksum, and license](extern/clang-resource/README.md) are included with the parser.
