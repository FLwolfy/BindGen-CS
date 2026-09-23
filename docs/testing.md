# BindGen-CS Testing Workflow

The test system separates fast managed feedback from target-specific release evidence. A passing unit test on one host is not treated as proof for another native ABI.

## Prerequisites

- .NET SDK 9.0 (`dotnet --version`)
- Clang/LibClang available for parser-dependent tests

## Choose the right test level

| Level | Command | Use it for |
| --- | --- | --- |
| Fast managed loop | `dotnet test BindGen-CS.sln -c Release` | Parser, configuration, analysis, emission, Runtime, and bridge regressions |
| Real C libraries | `./scripts/test-real-libraries.sh` | Deterministic snapshots plus warning-free IR-native generation/compilation |
| Real C++ bridge | `./scripts/test-real-cpp-libraries.sh` | Bridge generation, native compiler validation, C# rebound, snapshots |
| InnoEngine integration | `./scripts/test-innoengine-bindings.sh` | Deferred until BGCS desktop-x64 acceptance; not part of the current full matrix |
| NuGet/tool/native asset | `./scripts/test-nuget-packages.sh` | Deterministic pack, clean restore, tool install, RID asset load and native invocation |
| API compatibility | `./scripts/test-public-api-compatibility.sh` | Reviewed pre-release assembly baselines |
| Dependency policy | `./scripts/test-supply-chain-policy.sh` | NuGet advisory query and license inventory/deny policy |
| Performance | `./scripts/test-performance-budget.sh` | 10,000 declarations, cold generation and warm cache budgets |
| Release acceptance | `./scripts/run-full-test-matrix.sh` | Every mandatory gate and machine-readable report |

## One-command full matrix

```bash
./scripts/run-full-test-matrix.sh
```

Run the standalone commands from the table when iterating on one layer. Run the full matrix before treating the target as accepted.

The restore gates intentionally evaluate the MSBuild project graph on one node. This keeps `obj/project.assets.json` deterministic when the same checkout is mounted by different operating systems or architectures; compilation and native-library builds retain their normal parallelism.

The real-library matrix discovers a sibling read-only third-party corpus or uses `INNOENGINE_ROOT`. For miniaudio, SDL3, cimgui, cimguizmo, and bgfx it regenerates the sole IR-native surface, enforces generation budgets, and checks target-specific deterministic source plus reflection public-API snapshots. The full matrix requires these headers; the standalone script may set `REQUIRE_REAL_LIBRARIES=1` to make absence fatal. The real C++ matrix additionally generates a bimg C bridge, validates it with the discovered C++ driver, feeds the generated C header back into BGCS, compiles the C# consumer, and verifies target-specific bridge/source/public-API snapshots.

The real-library scripts read only pinned upstream headers; they do not regenerate or modify InnoEngine. The separate InnoEngine workspace gate is intentionally excluded until all required BGCS desktop-x64 reports pass.

The full script runs all major BGCS capabilities in ordered layers:

1. Core libraries (`BGCS.Core`, `BGCS.CppAst`, `BGCS.Language`, `BGCS.Runtime`)
2. BGCS base/unit/parser logic (`BGCS.Tests`)
3. Patch-specific behavior (`BGCS.Patching.Tests`)
4. Generated output compile/runtime semantics (`BGCS.Generation.Tests`)
5. `BGCS.Cpp2C` generation, deterministic build manifests, native build-provider plans, C++ syntax validation, DLL linking, and runtime invocation
6. CLI behavior (`init`, JSON Schema, diagnostic catalog, and native-build plans)
7. End-to-end demo generation (`runtime-generated` + `runtime-notgenerated`)
8. NuGet dependency-closure restore, deterministic pack, tool installation, and native-RID consumer invocation
9. Public API baseline plus dependency license/vulnerability policy

On success it writes `artifacts/acceptance/report.json` and `report.md`, and retains target-specific copies under `artifacts/acceptance/reports/<target>/`. Report generation fails if any mandatory marker is missing. Windows x64, Linux x64, and macOS x64 reports must be generated from the same revision before maintenance status. See [Acceptance](acceptance.md) for the scoring contract.

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
- deterministic, portable bridge build manifests and manifest validation
- shell-independent Clang/GNU build plans and actual host shared-library compilation
- configured STL adapters for string, vector, span, array, map, set, optional, variant, expected, path, chrono, unique_ptr, and shared_ptr
- managed virtual callback proxy generation
- multiple-inheritance cast adjustment
- clang++ bridge DLL linking and Create/Invoke/Destroy/error-channel runtime calls
- retained callback unregister/dispose races and async exactly-once cleanup

`tests/BGCS.Tool.Tests` covers:

- portable, config-relative `init` output and C/C++ language selection
- strict C/C++ JSON Schema generation and compatibility opt-out
- stable diagnostic lookup through `explain`
- versioned manifest validation and shell-independent `native-build --dry-run` plans

## CI usage

The repository workflow runs the managed solution and complete acceptance jobs for Windows x64, Linux x64, and Intel macOS x64. A target is accepted only after its job emits its own report; a configured job or managed-only pass is not a target acceptance report.

CI can invoke:

```bash
SKIP_RESTORE_BUILD=1 ./scripts/run-full-test-matrix.sh
```

when restore/build are already completed in earlier steps.
