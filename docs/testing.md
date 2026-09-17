# BindGen-CS Testing Workflow

This repository now includes a full test workflow modeled as a layered generation pipeline, similar to the generator-centric workflow used in Hexa-based projects.

## Prerequisites

- .NET SDK 9.0 (`dotnet --version`)
- Clang/LibClang available for parser-dependent tests

## One-command full matrix

```bash
./scripts/run-full-test-matrix.sh
./scripts/test-real-libraries.sh
./scripts/test-real-cpp-libraries.sh
```

The real-library matrix discovers a sibling InnoEngine checkout or uses `INNOENGINE_ROOT`. It regenerates and compiles miniaudio, SDL3, cimgui, and cimguizmo SingleFile bindings, enforces the Windows generation budgets, and checks deterministic generated-source fingerprints plus reflection-based public API snapshots in `tests/real-libraries/api-snapshots.sha256` and `tests/real-libraries/public-api-snapshots.sha256`. Set `REQUIRE_REAL_LIBRARIES=1` to fail instead of skipping when vendored headers are unavailable. The real C++ matrix additionally generates a bimg C bridge, validates it with clang++, feeds the generated C header back into BGCS, compiles the C# consumer, and verifies bridge/source/public-API snapshots.

The script runs all major BGCS capabilities in ordered layers:

1. Core libraries (`BGCS.Core`, `BGCS.CppAst`, `BGCS.Language`, `BGCS.Runtime`)
2. BGCS base/unit/parser logic (`BGCS.Tests`)
3. Patch-specific behavior (`BGCS.Patching.Tests`)
4. Generated output compile/runtime semantics (`BGCS.Generation.Tests`)
5. `BGCS.Cpp2C` generation, native C++ syntax validation, DLL linking, and runtime invocation
6. End-to-end demo generation (`runtime-generated` + `runtime-notgenerated`)
7. NuGet dependency-closure restore, consumer compilation, execution, and `bindgen-cs` tool installation

Demo artifacts are emitted under:

- `demo/BGCS.Demo/bin/<Configuration>/generated/OutputRuntimeGenerated`
- `demo/BGCS.Demo/bin/<Configuration>/generated/OutputRuntimeNotGenerated`

Demo semantics:

- `runtime-generated`: single-file bindings + standalone `Runtime.cs` (`GenerateRuntimeSource=true`)
- `runtime-notgenerated`: single-file bindings only (`GenerateRuntimeSource=false`)

## Feature Coverage Mapping

`tests/BGCS.Tests` covers:

- Core unit and parser-interop behavior for BGCS

`tests/BGCS.Patching.Tests` covers:

- Patch infrastructure (`PatchEngine`) behavior
- Multi-stage patch matrix (pre/post, file create/modify, chaining)
- Single-file merge compatibility with post-patch behavior

`tests/BGCS.Generation.Tests` covers:

- Function generation pipeline and regression matrix
- Generated source compile correctness under matrix configurations
- Function-table/custom-context runtime behavior of generated code

`tests/BGCS.Cpp2C.Tests` covers:

- C++ to C bridge generation semantics and metadata flow
- real MSVC STL adapters for string, span, optional, and unique_ptr
- managed virtual callback proxy generation
- multiple-inheritance cast adjustment
- clang++ bridge DLL linking and Create/Invoke/Destroy/error-channel runtime calls

## CI usage

CI can invoke:

```bash
SKIP_RESTORE_BUILD=1 ./scripts/run-full-test-matrix.sh
```

when restore/build are already completed in earlier steps.
