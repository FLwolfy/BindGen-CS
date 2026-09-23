# Acceptance Specification

[Wiki](README.md) | [中文](acceptance.cn.md)

This document is normative. A category score is generated from test artifacts; maintainers do not assign scores manually. The 9.0 value is a BindGen-CS internal release-gate level, not an external industry benchmark, complete C++ coverage percentage, or third-party audit score.

## General rule

Each category has an explicit set of automated mandatory gates. Every reported category scores **9.0/10.0** only when all of its gates pass on the release commit; report generation fails if any gate is absent. Reports are target-specific: a passing report proves only the platform, architecture, and ABI named in its `target` field.

The release workflow must write machine-readable results to `artifacts/acceptance/report.json` and a human-readable summary to `artifacts/acceptance/report.md`.

Both reports record their UTC generation time, Git revision, and working-tree dirty state. A dirty local report is valid development evidence, but it cannot masquerade as evidence for an immutable release commit.

## Target matrix

| Category | Target | Mandatory gates |
| --- | ---: | --- |
| Small C APIs | 9.0 | 100% generated compilation; ABI invocation; no source edits |
| Medium/large C APIs | 9.0 | SDL3, miniaudio, cimgui, cimguizmo, and bgfx deterministic snapshots plus warning-free IR-native compilation |
| Complex C ABI correctness | 9.0 | Host-native invocation and target-specific ABI/layout/calling-convention matrix |
| Ordinary C++ class bridge | 9.0 | Native compile/link/run for lifecycle and methods |
| Modern C++ | 9.0 | Selected templates/STL/smart pointers/virtual callbacks |
| Generated API quality | 9.0 | Source/public-API snapshots; analyzers; no native imports outside generated output |
| Beginner usability | 9.0 | Five-command maximum from header to validated output |
| External architecture | 9.0 | Intermediate/Runtime dependency-boundary tests and complete solution build |
| Internal architecture | 9.0 | Shared IR, analyzer/IR-emitter tests, and no pre-release fallback emitter |
| NuGet/testing/release | 9.0 | Clean packages/symbols, API and dependency policy, deterministic output, native-RID consumer, host-native and managed matrices |

## Real-library budgets

Measured on the reported host target after warm NuGet restore:

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
- UTF-8/UTF-16 strings with borrowed, owned, and caller-allocated lifetimes;
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
- native bridge compilation plus synthetic lifecycle/method runtime invocation.

Unsupported constructs must produce actionable diagnostics and an inspectable report; silently emitting an ABI-unsafe signature fails the category.

## API quality evidence

- Generated files are never manually edited.
- Library-specific behavior is represented by presets, policies, or patches stored outside generated output.
- Public API snapshots are reviewed and compiled.
- Raw imports remain available but friendly APIs use spans, strings, handles, results, and deterministic names where ownership facts permit.
- Generated source compiles with warnings as errors; documentation and lifetime details are emitted only when native declarations or explicit configuration provide those facts.

## Beginner workflow evidence

The following must succeed from a clean directory:

```bash
bindgen-cs init path/to/header.h
bindgen-cs doctor
bindgen-cs validate
bindgen-cs generate
bindgen-cs build
```

`doctor` reports toolchain problems, `validate` performs parsing and IR validation without replacing output, and `build` compiles generated C#. `bridge` emits a versioned native build manifest, and `native-build` can compile it with the built-in Clang/GNU-compatible provider; ordinary C# `build` remains separate. Structured safety/C++ rejections must include a suggested configuration path or source action.

## Architecture evidence

Current automated architecture tests directly enforce:

- Runtime has no generator dependency;
- Intermediate has no facade, emitter, Roslyn, filesystem, or CLI dependency;
- the Intermediate assembly references no other BGCS assembly;
- IR analysis and IR-native `CSharpEmitter.Emit` have independent behavior tests;
- unsupported IR-native C# semantics fail before output with stable `BGCSCS001` diagnostics.
- all five real C libraries regenerate through the IR-native backend and compile with warnings as errors; incomplete opaque storage passed by value is rejected instead of guessed.

Primary configured generation calls `CSharpEmitter` from canonical IR and covers raw plus string/span/ref/out friendly surfaces. No pre-release compatibility emitter or old-schema migration path exists.

## Package and release evidence

- All package IDs share one version.
- A clean local feed restores public packages and all transitive implementation packages.
- The consumer uses an isolated package cache; third-party packages already locked by solution restore are read only from the machine's global-packages fallback, so the release smoke does not depend on live nuget.org availability.
- The tool installs into an empty tool path and completes init/generate/build smoke tests.
- Two clean packs from the same commit produce equivalent package content after excluding NuGet signature metadata.
- A clean consumer selects and invokes the current host binary from `runtimes/<rid>/native/`.
- Reviewed API baselines, dependency licenses, known-vulnerability queries, and deterministic SPDX/SLSA payloads gate local release-candidate construction.
- A published release additionally requires the GitHub release job to obtain an OIDC identity and produce verifiable Sigstore attestations. Workflow presence is tested locally, but is never reported as an executed signature.
- Symbol packages and repository metadata are present.
- No release push runs before all mandatory gates pass.

## Current status

`scripts/run-full-test-matrix.sh` writes `artifacts/acceptance/report.json`, `report.md`, and target-retained copies only after every local BGCS gate above passes. The required maintenance-candidate desktop reports are Windows x64 MSVC, Linux x64 GNU, and macOS x64 Darwin; Windows additionally requires real clang-cl and MSBuild DLL build/export/invocation. These same-version reports remain pending until their runners complete. InnoEngine clean regeneration and its full native/build/test gate now pass separately on macOS Arm64 and are intentionally not folded into the BGCS score. A real OIDC/Sigstore signature likewise remains a release-run artifact, not local evidence.
