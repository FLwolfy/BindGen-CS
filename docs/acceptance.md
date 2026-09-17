# Acceptance Specification

[Wiki](README.md) | [中文](acceptance.cn.md)

This document is normative. A category score is generated from test artifacts; maintainers do not assign scores manually.

## General rule

Each category contains ten equally weighted evidence points. A point is awarded only when its automated gate passes on the release commit. The required range is **8.5-9.0**, calculated using half-points where a gate explicitly defines partial coverage. Any mandatory gate failure caps that category below 8.5. Cross-platform gates are deferred; the current required platform is Windows x64, with Windows x86 layout coverage where stated.

The release workflow must write machine-readable results to `artifacts/acceptance/report.json` and a human-readable summary to `artifacts/acceptance/report.md`.

## Target matrix

| Category | Target | Mandatory gates |
| --- | ---: | --- |
| Small C APIs | 8.5-9.0 | 100% generated compilation; ABI invocation; no source edits |
| Medium/large C APIs | 8.5-9.0 | SDL3, miniaudio, cimgui regeneration and compilation |
| Complex C ABI correctness | 8.5-9.0 | MSVC x86/x64 layout and calling-convention matrix |
| Ordinary C++ class bridge | 8.5-9.0 | Native compile/link/run for lifecycle and methods |
| Modern C++ | 8.5-9.0 | Selected templates/STL/smart pointers/virtual callbacks |
| Generated API quality | 8.5-9.0 | Inno.Native snapshots; analyzers; zero manual generated patches |
| Beginner usability | 8.5-9.0 | Five-command maximum from header to validated output |
| External architecture | 8.5-9.0 | Automated dependency-boundary test |
| Internal architecture | 8.5-9.0 | Shared IR and independently tested analyzers/emitters |
| NuGet/testing/release | 8.5-9.0 | Clean packages, symbols, deterministic repeat pack, full Windows matrix |

## Real-library budgets

Measured on the pinned Windows CI runner after warm NuGet restore:

| Library | Generate budget | Required result |
| --- | ---: | --- |
| cimguizmo | 15 seconds | C bridge and C# compile |
| cimgui | 30 seconds | C# compile and API snapshot |
| SDL3 | 45 seconds | C# compile and API snapshot |
| miniaudio split header | 60 seconds | C# compile and API snapshot |

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

- overloaded/default/deleted constructors and destructors;
- instance, const, static, virtual, pure virtual, and overloaded methods;
- namespace free functions and operators selected by policy;
- single, multiple, and virtual inheritance with generated pointer adjustments;
- exception capture and managed error propagation;
- explicit class/function template instantiations;
- configured adapters for string, vector, span, optional, unique/shared pointers, and selected variants;
- managed implementations of configured abstract callback interfaces;
- move-only and non-trivial return values lowered through safe bridge storage.

Unsupported constructs must produce actionable diagnostics and an inspectable report; silently emitting an ABI-unsafe signature fails the category.

## API quality evidence

- Generated files are never manually edited.
- Library-specific behavior is represented by presets, policies, or patches stored outside generated output.
- Public API snapshots are reviewed and compiled.
- Raw imports remain available but friendly APIs use spans, strings, handles, results, and deterministic names where ownership facts permit.
- XML documentation contains valid summary, parameter, type-parameter, return, ownership, and lifetime information.

## Beginner workflow evidence

The following must succeed from a clean directory:

```bash
bindgen-cs init path/to/header.h
bindgen-cs doctor
bindgen-cs validate
bindgen-cs generate
bindgen-cs build
```

`doctor` reports toolchain problems, `validate` performs parsing and IR validation without replacing output, and `build` compiles generated C# plus any C bridge. Every skipped declaration is summarized with a suggested config or source action.

## Architecture evidence

Automated architecture tests enforce:

- Runtime has no generator dependency;
- Intermediate has no facade, emitter, Roslyn, filesystem, or CLI dependency;
- Analysis has no emitter or CLI dependency;
- emitters depend on Intermediate contracts rather than concrete facades;
- Tool contains command composition only;
- `CsCodeGenerator` delegates application work and remains a compatibility facade;
- BGCS and Cpp2C share request, diagnostic, IR, output, and emission contracts.

## Package and release evidence

- All package IDs share one version.
- A clean local feed restores public packages and all transitive implementation packages.
- The tool installs into an empty tool path and completes init/generate/build smoke tests.
- Two clean packs from the same commit produce equivalent package content after excluding NuGet signature metadata.
- Symbol packages and repository metadata are present.
- No release push runs before all mandatory gates pass.

## Current status

The mandatory Windows x64 MSVC scope passes with every measured category between 8.5 and 9.0. `scripts/run-full-test-matrix.sh` writes `artifacts/acceptance/report.json` only after managed tests, native C/C++ runtime gates, four real-library generation/compilation budgets, source and reflection public-API snapshots, and NuGet/tool smoke tests pass. Other target ABIs and direct calls into upstream DLLs that are not built by this fixture are explicitly outside the verified claim.
