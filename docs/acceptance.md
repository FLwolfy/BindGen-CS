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
| Medium/large C APIs | 9.0 | SDL3, miniaudio, cimgui, cimguizmo, and bgfx regeneration/compilation plus InnoEngine workspace diff |
| Complex C ABI correctness | 9.0 | Host-native invocation and target-specific ABI/layout/calling-convention matrix |
| Ordinary C++ class bridge | 9.0 | Native compile/link/run for lifecycle and methods |
| Modern C++ | 9.0 | Selected templates/STL/smart pointers/virtual callbacks |
| Generated API quality | 9.0 | Source/public-API snapshots; analyzers; no native imports outside generated output |
| Beginner usability | 9.0 | Five-command maximum from header to validated output |
| External architecture | 9.0 | Intermediate/Runtime dependency-boundary tests and complete solution build |
| Internal architecture | 9.0 | Shared IR, analyzer/IR-emitter tests, and explicitly documented compatibility migration state |
| NuGet/testing/release | 9.0 | Clean packages, symbols, deterministic output, host-native and managed matrices |

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
- configured adapters for string, vector, span, blittable/non-blittable optional, and unique/shared pointers;
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

The primary configured path still calls the isolated `AstGenerationStepEmitter` and `GenerationStep`; `CSharpEmitter` no longer exposes or contains a legacy path. Architecture 9.0 therefore means that the declared boundary/build/test gates pass; it does not mean default-path IR migration is complete. See [Architecture](architecture.md#migration-completion-criteria) for completion criteria.

## Package and release evidence

- All package IDs share one version.
- A clean local feed restores public packages and all transitive implementation packages.
- The consumer uses an isolated package cache; third-party packages already locked by solution restore are read only from the machine's global-packages fallback, so the release smoke does not depend on live nuget.org availability.
- The tool installs into an empty tool path and completes init/generate/build smoke tests.
- Two clean packs from the same commit produce equivalent package content after excluding NuGet signature metadata.
- Symbol packages and repository metadata are present.
- No release push runs before all mandatory gates pass.

## Current status

The macOS arm64 Darwin scope passes every measured category at 9.0. `scripts/run-full-test-matrix.sh` writes `artifacts/acceptance/report.json` and `report.md` only after managed tests, native C/C++ runtime gates, five real C libraries, the bimg C++ bridge, deterministic source/reflection snapshots, the InnoEngine five-project workspace/native-dependency/build/native-test gate, and NuGet/tool smoke tests pass. Windows and other target ABIs require separate target-specific reports; no report is treated as proof for a different target.
