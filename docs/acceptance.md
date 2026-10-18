# Acceptance Specification

[Wiki](README.md) | [中文](acceptance.cn.md)

This document defines mandatory acceptance evidence. Results report pass or failure for explicit gates, rather than assigning a numeric quality score.

## General rule

Each category has explicit automated mandatory gates. A category passes only when every gate succeeds; report generation fails if any gate is absent. Reports are target-specific and prove only the platform, architecture and ABI named in the target field.

The release workflow must write machine-readable results to `artifacts/acceptance/report.json` and a human-readable summary to `artifacts/acceptance/report.md`.

Both reports record their UTC generation time, Git revision, and working-tree dirty state. A dirty local report is valid development evidence, but it cannot masquerade as evidence for an immutable release commit.

## Target matrix

| Category | Required result | Mandatory gates |
| --- | ---: | --- |
| Small C APIs | passed | 100% generated compilation; ABI invocation; no source edits |
| Medium/large C APIs | passed | SDL3, miniaudio, cimgui, cimguizmo, and bgfx deterministic snapshots plus warning-free IR-native compilation |
| Complex C ABI correctness | passed | Host-native invocation and target-specific ABI/layout/calling-convention matrix |
| Ordinary C++ class bridge | passed | Native compile/link/run for lifecycle and methods |
| Modern C++ | passed | Selected templates/STL/smart pointers/virtual callbacks |
| Generated API quality | passed | Source/public-API snapshots; analyzers; no native imports outside generated output |
| Beginner usability | passed | Five-command maximum from header to validated output |
| External architecture | passed | Contracts/Runtime independence, Contracts-only plugins and complete solution build |
| Internal architecture | passed | Thirteen-project dependency policy, neutral model stages, worker isolation and no old facade/configuration path |
| NuGet/testing/release | passed | Clean packages/symbols, API and dependency policy, deterministic output, native-RID consumer, host-native and managed matrices |

## Real-library budgets

Measured on a controlled, reported host target after warm NuGet restore. The real-library functional matrix reports generation times without failing on noisy shared-runner timing. Set `BGCS_ENFORCE_GENERATION_BUDGETS=1` for a dedicated real-library performance run that enforces these budgets; `scripts/test-performance-budget.sh` separately enforces the 10,000-declaration cold/warm cache budgets below.

The 10,000-declaration cache gate verifies emitted method count, byte-identical output after deleting the generated directory, and the complete sealed immutable artifact set. The current budgets in `eng/acceptance/performance-budgets.json` are 60 seconds cold / 10 seconds warm on Linux and Windows x64, and 120 seconds cold / 30 seconds warm on macOS Intel. ARM64 budgets belong to its independent certification. `BGCS_PERF_COLD_BUDGET_SECONDS` and `BGCS_PERF_WARM_BUDGET_SECONDS` may set stricter limits; reports record actual limits, worker and session peak memory. A C# emission cache hit alone never certifies parser or whole-pipeline reuse.

| Library | Generate budget | Required result |
| --- | ---: | --- |
| cimguizmo | 15 seconds | C bridge and C# compile |
| cimgui | 30 seconds | C# compile and API snapshot |
| SDL3 | 45 seconds | C# compile and API snapshot |
| miniaudio split header | 60 seconds | C# compile and API snapshot |
| bgfx C99 | 30 seconds | C# compile and API snapshot |
| bimg C++ | 15 seconds | C bridge, native syntax check, C# rebind, and API snapshot |

A timeout, out-of-memory result, unbounded cache growth, or manual deletion of declarations fails the medium/large C category.

## ABI evidence

The ABI suite must compile a native test DLL with the same headers and compare managed observations for:

- primitive sizes and signedness;
- enum underlying types;
- sequential, explicit, packed, nested, anonymous, and aligned records;
- one- and multi-dimensional fixed arrays and flexible array tails;
- signed, unsigned, zero-width, cross-unit, struct, and union bitfields;
- pointers, references, function pointers, callbacks, and user data;
- cdecl, stdcall, thiscall where applicable, and vectorcall diagnostics;
- UTF-8/UTF-16/UTF-32 strings, explicit byte/code-unit lengths, embedded zeros, and borrowed/owned/caller-allocated lifetimes;
- independent native layout, managed storage and by-value calling compatibility, including over-alignment and long-double pointer/shim alternatives;
- pointer/count, capacity/written-count, and two-call query patterns.

Generated compilation without native invocation is not sufficient ABI evidence.

## C++ evidence

The bridge suite must compile, link, and execute tests for:

- class lifecycle plus instance/static/overloaded methods;
- namespace free functions and exception boundaries;
- multiple inheritance with generated pointer adjustments;
- exception capture and managed error propagation;
- explicit class/function template instantiations;
- configured lowerings for string, vector, span, array, map, set, optional, variant, expected, path, chrono, and unique/shared pointers;
- allocator/deallocator pairing, retained callback unregister/drain races, and async completion lifetime;
- managed implementations of configured abstract callback interfaces;
- actual native bridge compilation and managed-consumer lifecycle/method invocation.

Unsupported constructs must produce actionable diagnostics and an inspectable report; silently emitting an ABI-unsafe signature fails the category.

## API quality evidence

- Generated files are never manually edited.
- Library-specific behavior is represented by presets, policies, or patches stored outside generated output.
- Public API snapshots are reviewed and compiled.
- Source snapshots compare generated C# byte-for-byte except for the header's `ABI reference target` line, which identifies parsing provenance rather than the binding contract. Any other whitespace or body change still requires a reviewed baseline update.
- Raw imports remain available but friendly APIs use spans, strings, handles, results, and deterministic names where ownership facts permit.
- Generated source compiles with warnings as errors; documentation and lifetime details are emitted only when native declarations or explicit configuration provide those facts.

## Beginner workflow evidence

The following must succeed from a clean directory:

```bash
bindgen-cs init path/to/header.h
bindgen-cs doctor
bindgen-cs validate
bindgen-cs generate
bindgen-cs generate
```

`doctor` reports toolchain and resource problems; `validate` checks configuration and neutral contracts without replacing output. `generate` follows the unified C/C++ pipeline, including a C++ bridge when required. `bridge` emits native bridge sources; consumer compilation, native linking and deployment use external builds. Safety/C++ rejections identify a concrete configuration or source repair.

For example, parser results using a GNU SDK declaration prelude require the selected native compiler to independently check types, layout and constants before publication, including through `generate` and `bridge`.
Reports distinguish product construction/linking from the actual `nativeFactProofRequired` and `nativeFactProof` results.
A fact proof is not product-linking or invocation evidence.

## Architecture evidence

Current automated architecture tests directly enforce:

- Runtime and Contracts each reference no other BGCS assembly;
- Contracts exposes no frontend, Roslyn, filesystem, MSBuild or CLI types;
- standard lowering and C# emission do not reference the parser implementation;
- the default Host crosses a checked worker process boundary before consuming detached NativeModule facts;
- NativeModule, CppBridgeModule, InteropModule, ManagedApiModel and CSharpEmissionPlan have distinct ownership and independent tests;
- the C# backend validates planned and actual import/storage contracts before publication; unknown by-value ABI is rejected;
- the real C corpus and bimg C++ path regenerate and compile with warnings as errors without editing generated output;
- production Compile Link sharing and production friend assemblies are rejected. The one registered frontend-test friend is constrained to a real non-packable test project.

CLI, embedded SDK, workspace and MSBuild call the public BindingHost pipeline. CSharpBackend consumes managed/interop contracts and a checked emission plan. There is no old CsCodeGenerator facade, BindingModule, flat configuration reader, migration tool or compatibility emitter. Model-specific extension ports and the advanced Clang-only worker port are documented separately.

## Package and release evidence

- All package IDs share one version.
- A clean local feed restores the four products and their declared technical parser/tool partitions; internal projects are not separately installed products.
- Consumers use isolated product caches and exact archive hashes. Any declared third-party fallback cannot substitute previously installed BGCS products. Public-release smoke separately installs from the actual public NuGet source.
- Package and source Runtime consumers include minimal and complete source closures, single-file output, independent namespaces and no generator references.
- The tool installs into an empty tool path and completes init/generate/build smoke tests.
- Two clean packs from the same commit produce equivalent package content after excluding NuGet signature metadata.
- A clean consumer selects and invokes the current host binary from `runtimes/<rid>/native/`.
- Reviewed API baselines, dependency licenses, known-vulnerability queries, and deterministic SPDX/SLSA payloads gate local release-candidate construction.
- A published release additionally requires the GitHub release job to obtain an OIDC identity and produce verifiable Sigstore attestations. Workflow presence is tested locally, but is never reported as an executed signature.
- Symbol packages and repository metadata are present.
- No release push runs before all mandatory gates pass.

## Current status

`scripts/run-full-test-matrix.sh` writes host acceptance reports only after its mandatory gates succeed. Complete release requirements are data in `eng/acceptance/required-release-gates.json`: same-revision desktop reports, browser interpreter/AOT, NativeAOT, full installed products and Visual Studio 2026 TaskHost evidence. Host tests use exact structured discovery, eight disjoint class shards and a complete raw/TRX union; missing, extra, duplicate, skipped or failed cases cannot certify the suite. Dirty local receipts remain development evidence. See [review and certification states](release-evidence-state-flow.md); authenticated preflight, actual attestations and verified public installation cannot be replaced by a checked-in completion claim.
