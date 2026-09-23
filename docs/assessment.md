# BindGen-CS Engineering Maturity Assessment

[简体中文](assessment.cn.md) | [Capability matrix](capabilities.md) | [Acceptance](acceptance.md) | [Roadmap](roadmap.md)

Assessment date: 2026-09-23.

## Verdict

BindGen-CS is a **strong maintenance-candidate C/C++ to C# toolchain within its declared semantic scope**, not a thin header-to-`DllImport` script. Its sole C# path is IR-native; advanced desktop C++ adapters, lifetime contracts, real native invocation, deterministic packaging, API/dependency gates, and signed-release automation are implemented.

The current `macos-arm64-darwin` report passes all ten mandatory categories at 9.0/10.

It is not yet honest to call the current revision fully maintained or universally production-supported. Windows x64, Linux x64, and macOS x64 must each publish the same-version complete report; Windows must additionally prove clang-cl and MSBuild runtime invocation. InnoEngine migration intentionally starts only after those reports pass.

## Quantified assessment

| Dimension | Current assessment | Evidence and remaining deduction |
| --- | ---: | --- |
| Supported C ABI correctness | 9.0/10 code quality | Real libraries, layout, compilation, invocation, snapshots, and strict diagnostics; desktop-x64 report evidence remains pending |
| Supported C++ Bridge subset | 9.0/10 code quality | Class lifecycle, inheritance adjustment, specializations, requested STL adapters, smart pointers, and callbacks are tested; unknown semantics fail closed |
| Usability | 9.0/10 | `init → doctor → validate → generate → build`, workspace, schema, explain, config-relative paths, and transactional output |
| Extensibility | 9.1/10 | v1 plugin contract, type/callable adapter SPI, deterministic priority, dependency isolation, atomic registration, and cache fingerprints |
| Performance and determinism | 9.0/10 | 10,000-declaration cold/warm budgets, content-addressed cache, concurrent publication, output restoration, and stable hashes |
| InnoEngine automation | Deferred | Deliberately excluded until BGCS desktop-x64 maintenance gates pass |
| Architecture completion | 9.0/10 | IR-native raw/friendly emission is the sole path; pre-release fallback and old config migration are removed |
| Global cross-platform evidence | Not accepted yet | Windows x64, Linux x64, and macOS x64 same-version reports remain mandatory |
| Arbitrary C++ coverage | Explicitly bounded | The controlled subset is strong; arbitrary metaprogramming and undeclared allocator/container semantics are rejected |
| Release supply chain | 9.0/10 automation | Deterministic NuGet, native-RID consumer, SPDX/SLSA, API/license/vulnerability gates, and OIDC signing |

These values are not averaged into a substitute release score. The [acceptance specification](acceptance.md) still requires every category for every declared target to reach 9.0 independently.

## Honest status of the five current workstreams

| Workstream | Status | Existing acceptance | Required before closure |
| --- | --- | --- | --- |
| Fully IR-native C# path | Complete | Raw ABI, string/span/ref/out friendly surface, fail-closed rollback, schema/init tests; old emitter deleted | Continue expanding real-library API snapshots |
| Formal C++ type/callable adapter SPI | v1 complete | External-assembly E2E, API shape, deterministic order, conflict rejection, built-ins in the same registry, all callable kinds | Future adapters may extend but must preserve the v1 contract |
| Native build providers and export inspection | Desktop artifact orchestration complete | Provider/export gates plus NuGet `runtimes/<rid>/native` and SHA-256 index | Execute clang-cl/MSBuild runtime gates on Windows |
| Desktop-x64 acceptance | Automated, evidence pending | Dedicated GNU/MSVC/Darwin x64 runners and target-isolated reports | All three jobs must pass on the same revision; dry-run or cross compilation cannot count |
| Incremental cache, large-project budget, stable plugin contracts | v1 complete | Fingerprints, atomic immutable cache, concurrency/10k/plugin E2E, and formal obsolete window | Workspace DAG, shared parser cache, and memory trends are vNext extensions |

## Why it is already strong

- Correctness comes first: missing ownership, allocator, callback-lifetime, or C++-lowering evidence produces stable diagnostics instead of plausible but wrong ABI code.
- The engineering loop covers generation, compilation, native invocation, API snapshots, deterministic packages, clean native consumers, and supply-chain policy.
- No requested high-risk feature uses a native-library-name branch; semantics are expressed through reusable configuration, mappings, adapters, providers, and gates.
- Third-party plugins and C++ adapters use versioned contracts; binary content, versions, and state participate in cache keys.
- Target, ABI, triple, sysroot, compiler, generated output, snapshots, and reports are isolated, preventing one host pass from masquerading as evidence for another ABI.

## Hard gates for the “super-universal” claim

1. Run the same complete matrix on Windows x64, Linux x64, macOS x64, then Windows Arm64; execute clang-cl/MSBuild providers on Windows.
2. Retain an independent current-version artifact for every production target; Android/iOS/FreeBSD are formal support targets and remain ⚠️ until implementation and acceptance are complete.
3. After desktop-x64 acceptance, execute clean InnoEngine regeneration and remove handwritten bindings.
4. Treat workspace DAG/shared parser caches as later performance evolution, not a blocker for the declared single-workspace feature set.

The precise product statement is therefore: **BindGen-CS is already architecturally strong, powerful, and easy to operate inside its explicit C/C++ contract, but it becomes a maintenance release only after the three desktop-x64 reports and then the separate InnoEngine migration.**
