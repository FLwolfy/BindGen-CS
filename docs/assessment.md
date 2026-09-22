# BindGen-CS Engineering Maturity Assessment

[简体中文](assessment.cn.md) | [Capability matrix](capabilities.md) | [Acceptance](acceptance.md) | [Roadmap](roadmap.md)

Assessment date: 2026-09-22.

## Verdict

BindGen-CS is already a **production-grade C/C++ to C# toolchain within its declared scope**, not merely a header-to-`DllImport` script. The complete `macos-arm64-darwin` matrix scores 9.0/10.0 in all ten acceptance categories. InnoEngine's five native-binding projects regenerate from configuration and pass native-dependency builds, the managed solution build, native binding tests, and the handwritten-import audit.

It is not yet honest to describe the product as a zero-configuration translator for arbitrary C++ on every platform. Target-scoped 9.0 acceptance and global platform/language maturity are different measures. The latter is still limited by the default C# path not being fully IR-native and by the lack of equivalent current-version reports from Windows and Linux.

## Quantified assessment

| Dimension | Current assessment | Evidence and remaining deduction |
| --- | ---: | --- |
| Supported C ABI correctness | 9.0/10 | Real libraries, layout, compilation, invocation, snapshots, and strict diagnostics are in the macOS gate |
| Supported C++ Bridge subset | 9.0/10 | Class lifecycle, inheritance adjustment, explicit templates, selected STL, smart pointers, and callback proxies are tested; unknown semantics fail closed |
| Usability | 9.0/10 | `init → doctor → validate → generate → build`, workspace, schema, explain, config-relative paths, and transactional output |
| Extensibility | 9.1/10 | v1 plugin contract, type/callable adapter SPI, deterministic priority, dependency isolation, atomic registration, and cache fingerprints |
| Performance and determinism | 9.0/10 | 10,000-declaration cold/warm budgets, content-addressed cache, concurrent publication, output restoration, and stable hashes |
| InnoEngine automation | 9.0/10 | Five-project diff, all native dependencies, solution build, six native test projects, and handwritten-import audit |
| Architecture completion | 8.4/10 | The target layers are clear and `CSharpEmitter` consumes only IR; some default output semantics still use the isolated `AstGenerationStepEmitter` |
| Global cross-platform evidence | 7.0/10 | Target/ABI/provider/CI models exist; only macOS arm64 has a complete report for this version |
| Arbitrary C++ coverage | 7.5/10 | The controlled subset is strong; arbitrary metaprogramming and allocator/container combinations are intentionally not guessed |
| Release supply chain | 8.6/10 | Deterministic NuGet, clean consumers, and tool workflows pass; SBOM, provenance, and a formal compatibility window remain |

These values are not averaged into a substitute release score. The [acceptance specification](acceptance.md) still requires every category for every declared target to reach 9.0 independently.

## Honest status of the five current workstreams

| Workstream | Status | Existing acceptance | Required before closure |
| --- | --- | --- | --- |
| Fully IR-native default C# path | In progress | AST-free `CSharpEmitter`, fail-closed unsupported IR, architecture tests | Represent constants, delegates, aliases, bitfields, handles, friendly overloads, and import modes in canonical IR; remove `AstGenerationStepEmitter` from the default path |
| Formal C++ type/callable adapter SPI | v1 complete | External-assembly E2E, API shape, deterministic order, conflict rejection, built-ins in the same registry, all callable kinds | Future adapters may extend but must preserve the v1 contract |
| Native build providers and export inspection | Core complete | Direct Clang/GNU and CMake compile/export verification on macOS; deterministic clang-cl/Meson/MSBuild plan tests | Execute provider runtime gates on their Windows/Linux hosts and add multi-RID artifact layout |
| Windows/Linux acceptance equal to macOS | Open | CI jobs, target isolation, Windows snapshots, and managed models exist | Produce complete current-version reports on Windows and Linux; dry-run or cross compilation cannot count as runtime pass |
| Incremental cache, large-project budget, stable plugin contracts | v1 complete | Compiler/plugin/adapter/input fingerprints, atomic immutable cache, concurrency tests, 10k cold/warm gate, external-plugin E2E | Workspace DAG, shared parser cache, memory trends, and an obsolete window are vNext extensions rather than blockers to v1 |

## Why it is already strong

- Correctness comes first: missing ownership, allocator, callback-lifetime, or C++-lowering evidence produces stable diagnostics instead of plausible but wrong ABI code.
- The engineering loop covers generation, compilation, native invocation, API snapshots, package consumers, and a real engine integration.
- InnoEngine requirements are expressed through reusable configuration, mappings, adapters, providers, and gates—not native-library-name branches.
- Third-party plugins and C++ adapters use versioned contracts; binary content, versions, and state participate in cache keys.
- Target, ABI, triple, sysroot, compiler, snapshots, and reports are isolated, preventing a macOS pass from masquerading as Windows/Linux evidence.

## Hard gates for the “super-universal” claim

1. Complete canonical-IR parity for the default C# surface and remove default AST compatibility emission.
2. Run the same complete matrix on Windows x64 and Linux x64, then expand to arm64, Android, iOS, and FreeBSD.
3. Broaden native-tested C ABI combinations and adapters such as `map/set/array/variant/expected/path/chrono`, without guessing allocator or lifetime semantics.
4. Finish multi-RID native asset layout, workspace DAG/parallelism, the formal plugin obsolete window, SBOM, and provenance.
5. Retain a target-specific, current-version acceptance artifact for every production-support claim.

The precise product statement is therefore: **BindGen-CS is already excellent, powerful, and usable for real large projects on macOS and within its explicitly supported C/C++ subset. It is becoming a super-universal tool, but that claim must wait for the IR migration and Windows/Linux evidence.**
