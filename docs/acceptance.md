# Acceptance Specification

[Wiki](README.md) | [中文](acceptance.cn.md)

This document is normative. A category score is generated from test artifacts; maintainers do not assign scores manually.

## General rule

Each category has an explicit set of automated mandatory gates. Every reported category scores **9.0/10.0** only when all of its gates pass on the release commit; report generation fails if any gate is absent. Reports are target-specific: a passing report proves only the platform, architecture, and ABI named in its `target` field.

The release workflow must write machine-readable results to `artifacts/acceptance/report.json` and a human-readable summary to `artifacts/acceptance/report.md`.

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
| External architecture | 9.0 | Automated dependency-boundary test |
| Internal architecture | 9.0 | Shared IR and independently tested analyzers/emitters |
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

The macOS arm64 Darwin scope passes every measured category at 9.0. `scripts/run-full-test-matrix.sh` writes `artifacts/acceptance/report.json` and `report.md` only after managed tests, native C/C++ runtime gates, five real C libraries, the bimg C++ bridge, deterministic source/reflection snapshots, the InnoEngine five-project workspace/native-dependency/build/native-test gate, and NuGet/tool smoke tests pass. Windows and other target ABIs require separate target-specific reports; no report is treated as proof for a different target.
