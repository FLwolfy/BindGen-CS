# BindGen-CS Testing Workflow

The test system separates fast managed feedback from target-specific release evidence. A passing unit test on one host is not treated as proof for another native ABI.

## Prerequisites

- .NET SDK 9.0 (`dotnet --version`)
- Clang/LibClang available for parser-dependent tests

## Choose the right test level

| Level | Command | Use it for |
| --- | --- | --- |
| Fast managed loop | `dotnet test BindGen-CS.sln -c Release` | Parser, configuration, analysis, emission, Runtime, and bridge regressions |
| Real C libraries | `./scripts/test-real-libraries.sh` | Generation budgets, compile checks, deterministic source/API snapshots |
| Real C++ bridge | `./scripts/test-real-cpp-libraries.sh` | Bridge generation, native compiler validation, C# rebound, snapshots |
| InnoEngine integration | `./scripts/test-innoengine-bindings.sh` | Workspace diff, import audit, native builds, full solution, native tests |
| NuGet/tool | `./scripts/test-nuget-packages.sh` | Deterministic pack, clean restore, tool install and commands |
| Release acceptance | `./scripts/run-full-test-matrix.sh` | Every mandatory gate and machine-readable report |

## One-command full matrix

```bash
./scripts/run-full-test-matrix.sh
```

Run the standalone commands from the table when iterating on one layer. Run the full matrix before treating the target as accepted.

The real-library matrix discovers a sibling InnoEngine checkout or uses `INNOENGINE_ROOT`. It regenerates and compiles miniaudio, SDL3, cimgui, cimguizmo, and bgfx SingleFile bindings, enforces host-independent generation budgets, and checks target-specific deterministic source plus reflection public-API snapshots. The full matrix requires these headers; the standalone script may set `REQUIRE_REAL_LIBRARIES=1` to make absence fatal. The real C++ matrix additionally generates a bimg C bridge, validates it with the discovered C++ driver, feeds the generated C header back into BGCS, compiles the C# consumer, and verifies target-specific bridge/source/public-API snapshots.

The InnoEngine workspace gate then verifies that all five checked-in binding outputs are reproducible from `native/bindings/workspace.json`, rejects hand-authored native imports outside `Generated/`, builds every required native dependency from its pinned source, builds the full engine solution, and runs every project under `tests/native`.

The full script runs all major BGCS capabilities in ordered layers:

1. Core libraries (`BGCS.Core`, `BGCS.CppAst`, `BGCS.Language`, `BGCS.Runtime`)
2. BGCS base/unit/parser logic (`BGCS.Tests`)
3. Patch-specific behavior (`BGCS.Patching.Tests`)
4. Generated output compile/runtime semantics (`BGCS.Generation.Tests`)
5. `BGCS.Cpp2C` generation, native C++ syntax validation, DLL linking, and runtime invocation
6. End-to-end demo generation (`runtime-generated` + `runtime-notgenerated`)
7. NuGet dependency-closure restore, consumer compilation, execution, and `bindgen-cs` tool installation

On success it writes `artifacts/acceptance/report.json` and `report.md`. Report generation fails if any mandatory gate marker is missing. See [Acceptance](acceptance.md) for the scoring contract.

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
- configured STL adapters for string, vector, span, optional, unique_ptr, and shared_ptr
- managed virtual callback proxy generation
- multiple-inheritance cast adjustment
- clang++ bridge DLL linking and Create/Invoke/Destroy/error-channel runtime calls

## CI usage

The repository workflow runs the managed solution on Windows, Linux, and macOS, and runs the complete acceptance job on its declared macOS arm64 target. Managed cross-platform CI is not a substitute for a target-specific native acceptance report.

CI can invoke:

```bash
SKIP_RESTORE_BUILD=1 ./scripts/run-full-test-matrix.sh
```

when restore/build are already completed in earlier steps.
