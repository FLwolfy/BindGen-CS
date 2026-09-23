# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

BindGen-CS is a production-oriented, cross-platform C/C++ to C# binding toolchain. It generates C# interop for C ABIs and can turn C++ classes, explicit template instances, and selected STL types into an ABI-stable C bridge with matching C# bindings.

> **Verified status:** the current source has a complete passing `macos-arm64-darwin` report with all ten acceptance categories at 9.0/10. Every other platform listed below is a formal BindGen-CS support target, but remains ⚠️ until its implementation, packaging, and current-version host evidence are complete; evidence from one platform never substitutes for another. BindGen-CS diagnoses semantics it cannot prove instead of guessing C++ ABI, ownership, or allocator behavior.

## Support status

Legend: completely verified ✅　implementation or host acceptance pending ⚠️. Every listed platform is a support target; promotion to ✅ requires implementation, packaging, tests, and a current-version host report.

| Capability / target | Status | Boundary and evidence |
| --- | :---: | --- |
| Default IR-native C# raw + string/span/ref/out friendly surface | ✅ | The only C# emission path; pre-release legacy backends and config migration were removed |
| Multi-RID native package layout | ✅ | `runtimes/<rid>/native/` for win/linux/osx x64/arm64 plus clean consumer invocation |
| SBOM, provenance, API/license/vulnerability gates | ✅ | Deterministic SPDX/SLSA payloads and release gates; see the [pre-release policy](docs/compatibility-policy.md) |
| OIDC/Sigstore signed release execution | ⚠️ | The release workflow is implemented and requests GitHub OIDC; a real signed attestation can only be produced by an authorized GitHub release run and has not been claimed by this local report |
| `map/set/array/variant/expected/path/chrono` lowerings | ✅ | Compiled and invoked native bridge tests; unsupported specializations fail closed |
| Final C++ extension architecture | ✅ | Built-ins, declarative recipes, typed lowering plugins, explicit C shims, managed/native artifacts, and auditable safety bypass share one registry |
| Complex inheritance/specialization and lifetime contracts | ✅ | Native pointer-adjustment/template tests plus allocator/callback/async models and race tests |

## Platform support and acceptance

| Platform / architecture | Status | Current evidence and promotion requirement |
| --- | :---: | --- |
| macOS arm64 | ✅ | Complete `macos-arm64-darwin` report passed; all ten mandatory categories score 9.0/10 |
| Windows x64 | ⚠️ | Runner and clang-cl/MSBuild/DLL tests are configured; same-version complete host report and NuGet native consumer result are pending |
| Linux x64 | ⚠️ | Runner is configured; same-version complete host report and NuGet native consumer result are pending |
| macOS x64 | ⚠️ | Intel runner is configured; same-version complete host report and NuGet native consumer result are pending |
| Windows arm64 | ⚠️ | Target and desktop RID model exist; provider, native invocation, and complete report remain to be verified |
| Linux arm64 | ⚠️ | Target and desktop RID model exist; a current-version independent complete report is required |
| Android (arm/arm64/x64) | ⚠️ | Formal support target; target model exists, while NDK/sysroot, package layout, and device/emulator runtime acceptance remain |
| iOS (device/simulator) | ⚠️ | Formal support target; target model exists, while Xcode SDK, framework/XCFramework layout, and device/simulator acceptance remain |
| FreeBSD (x64/arm64) | ⚠️ | Formal support target; toolchain, package layout, and independent runtime report remain |

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

`init native.h` creates an immediately runnable, portable configuration. Header and include paths are written relative to the configuration file and use `/` separators, so the same file can be committed and used on Windows, macOS, and Linux:

```json
{
  "ConfigVersion": 1,
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
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --package-root package
```

This emits a C ABI wrapper; the default `init` configuration also generates C# bindings from the bridge header. `bridge.manifest.json` records generated/original sources, include directories, definitions, compiler/linker arguments, language standard, libraries, and resolved target. `native-build --provider auto|clang|clang-cl|cmake|meson|msbuild` consumes it as a shell-independent one- or multi-step pipeline. Successful builds verify every generated `API(...)` declaration against the actual `nm`/`dumpbin` export table by default. `--package-root` then stages the binary under standard multi-RID `runtimes/<rid>/native/` layout with a SHA-256 asset index. Use `--no-verify-exports` only when another release gate owns that check.

Verified C++ coverage includes construction/destruction, instance and static methods, overloads, namespace functions, exception boundaries, multiple-inheritance pointer adjustment, full/partial template specializations, explicit template instances, `std::string`, `vector`, `span`, `array`, `map`, `set`, `optional`, `variant`, `expected`, `filesystem::path`, `chrono` duration/time-point, smart pointers, and configured pure-virtual callback proxies. Complex project semantics can be added through declarative lowering recipes, a typed lowering plugin, or an explicit C ABI shim. Unproven rules fail by default; `LoweringSafetyPolicy=AllowUnsafe` is an explicit, diagnosed risk transfer rather than silent guessing. See the [final lowering architecture](docs/lowering.md).

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
| `schema` | Generate strict C or C++ JSON Schema from the installed version |
| `explain` | List or explain stable diagnostic codes in text or JSON |
| `bridge` | Generate a configuration-driven C++ to C bridge |
| `native-build` | Compile a bridge manifest into a target shared library, or inspect the plan with `--dry-run` |
| `supply-chain` | Generate SPDX 2.3 SBOM and SLSA v1 provenance for package/native artifacts |

See [Getting started](docs/getting-started.md) for complete examples and the [Configuration guide](docs/configuration-guide.md) for configuration decisions.

## Core capabilities

- Explicit platform, architecture, ABI, target-triple, sysroot, and compiler discovery.
- `DllImport`, `LibraryImport`, and explicit function-table/native-context import modes.
- Structs, unions, packing, bitfields, fixed arrays, typedefs, opaque handles, callbacks, and target-dependent primitives.
- `MarshallingMappings` for string encoding, ownership, cleanup, pointer/count, capacity/written-count, and caller allocation.
- `ExternalTypeContracts` for project-supplied managed ABI carriers, with target size/alignment checks and a per-type reject/match/bypass policy.
- The canonical `BindingModule` is the only C# emission input and drives both raw ABI and public string/span/ref/out friendly overloads. Unrepresentable semantics fail before commit, and output replacement remains transactional.
- BaseConfig composition, presets, single-file output, workspaces, deterministic diff, and target-specific snapshots.
- Content-addressed incremental output caching with exact input, compiler/toolchain, config, plugin, lowering, and shim fingerprints; atomic publication/restoration, target isolation, and concurrent-writer tests. Stateful custom extensions conservatively disable cache restoration unless their behavior has a stable fingerprint.
- One final C++ lowering extension architecture: declarative type/callable recipes, typed `ICppTypeLowering` / `ICppCallableLowering` / `ICppArtifactContributor` plugins, explicit C shims, isolated dependency resolution, deterministic registration, and reviewed API-shape tests. The deleted pre-release adapter contract has no compatibility wrapper.
- Separate CLI, generator, C++ bridge, Runtime, and dependency-free IR packages.

## Safety contract

BindGen-CS separates native declarations into three groups:

1. ABI and lifetime are sufficiently defined: generate and compile-check automatically.
2. Ownership, allocator, length, or callback lifetime is missing: emit an actionable `BGCS-SAFETY-*` diagnostic with the minimum configuration path.
3. A C++ type has no accepted lowering: add a recipe/plugin/shim, reject it with `BGCSCPP001` / `BGCSCPP-INSTANTIATION`, or deliberately continue under `AllowUnsafe` and retain the `BGCS-SAFETY-LOWERING-BYPASS` audit diagnostic.

That boundary is a correctness feature, not silent feature inflation. See the [diagnostics guide](docs/diagnostics.md).

## InnoEngine integration

InnoEngine now owns five BindGen-CS configurations for miniaudio, SDL3, cimgui, cimguizmo, and bgfx. A single workspace cleanly regenerates target-scoped output; the acceptance gate rejects handwritten imports outside `Generated/`, builds all pinned native dependencies, builds the full engine solution, and runs all six native-binding test projects. The current macOS Arm64 run passed this complete gate. This integration lives only in the InnoEngine repository; BindGen-CS core contains no InnoEngine library-name or path special case. The still-pending desktop-x64 reports remain independent BGCS release requirements.

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

Stable entry point:

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
if (!generator.GenerateConfigured())
{
    foreach (var diagnostic in generator.Messages)
        Console.Error.WriteLine(diagnostic);
}
```

Use `BGCS.Facade.BindingGenerator` when you need the structured IR. See [Architecture](docs/architecture.md) for the IR-native path and layer ownership.

## Acceptance

```bash
./scripts/run-full-test-matrix.sh
```

Reports are produced only after managed tests, native ABI/runtime gates, advanced C++/lifetime semantics, real C/C++ libraries, API and deterministic snapshots, clean NuGet/tool/native-RID consumers, license/vulnerability policy, and the 10,000-declaration cold/warm performance budget all pass:

- `artifacts/acceptance/report.json`
- `artifacts/acceptance/report.md`
- `artifacts/acceptance/reports/<target>/report.{json,md}`

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
- [Universal execution roadmap](docs/roadmap.md)
- [Engineering maturity assessment](docs/assessment.md)
- [Acceptance specification](docs/acceptance.md)
- [Testing](docs/testing.md)

## License

BindGen-CS is licensed under the MIT License. See [LICENSE](LICENSE). Portions derived from CppAst/HexaGen retain their original notices.
