# Universal BindGen Execution Roadmap

[中文](roadmap.cn.md) | [Capability matrix](capabilities.md) | [Acceptance specification](acceptance.md)

## End state

“Universal” does not mean guessing unknown C++ semantics. BindGen-CS is complete when it generates correct code under explicit target, ABI, ownership, and build contracts; declarative lowerings, versioned plugins, and explicit C shims extend project semantics; and unproven rules either fail with stable diagnostics or proceed only through an explicit, audited bypass.

Every dimension below must score at least 9.0/10.0. An average score cannot hide a failed dimension.

| Dimension | 9.0 acceptance condition |
| --- | --- |
| C ABI completeness | Common declaration, layout, callback, and variadic patterns have native compile/layout/invocation evidence |
| C++ bridge | Every declared class/template/STL/ownership feature has native invocation evidence; unknown semantics are rejected |
| Managed API quality | Raw ABI is correct and every friendly API has auditable ownership, encoding, length, and cleanup contracts |
| Usability | A new project completes `init → doctor → validate → generate → build`; failures identify an exact config repair path |
| Portability | Every production target has its own acceptance artifact; model support is not counted as host verification |
| Architecture | Parser, analysis, IR, emitters, Runtime, CLI, and build providers have one-way dependencies enforced by tests |
| Extensibility | A type/callable/artifact lowering, emitter, or build provider can be added without unrelated core changes or library-name special cases |
| Workspace integration | Multi-project validation, generation, diff, and clean consumer builds work without project-specific branches in BGCS |
| Performance | Cold/warm budgets, cache correctness, deterministic hashes, and concurrency safety are release gates |
| Release supply chain | Deterministic packages, SBOM/provenance, compatibility policy, and clean consumers pass |

## Non-negotiable rules

1. Core code never branches on a native library name.
2. Library semantics enter through general configuration, lowering/provider contracts, explicit C shims, or reusable analysis rules.
3. Correct raw ABI takes priority over the number of friendly overloads; uncertain lifetime retains raw access and emits a diagnostic.
4. Target support binds compiler, triple, sysroot, ABI, and real compile/run evidence.
5. Generated output is transactional; customization lives in configuration, plugins, or separate partial files.
6. New features need positive and negative coverage plus snapshot or compile coverage; ABI features require a native test.

## Phases

Current priority is feature-first and platform-later:

1. default IR-native friendly C# surface (implemented; API snapshots continue);
2. multi-RID native asset/package layout (implemented);
3. SBOM, provenance, and pre-release compatibility governance (implemented);
4. broader C/C++ semantics, lowerings, and lifetime contracts (implemented for the declared subset);
5. independent Windows x64, Linux x64, macOS x64, then optional Windows Arm64 reports;
6. Android/iOS/FreeBSD are formal support targets; add their toolchains/sysroots, package layouts, and host/device reports in sequence, retaining ⚠️ until complete.

### Phase 0 — Measurable baseline

Status: complete for BGCS. Ten acceptance categories, real-library gates, package/native-consumer tests, and evidence-level documentation are in place.

Exit: every capability claim links to a test/artifact or an explicit boundary.

### Phase 1 — Frictionless CLI and configuration contract

Status: complete for the pre-release contract. It includes portable `init`, explicit language selection, strict C/C++ schemas, a single current ConfigVersion with no old-schema migration, a versioned diagnostic catalog with `explain`, dedicated Tool tests, explicit configuration-directory resolution, and BaseConfig cycle detection without process-CWD mutation.

- Keep command behavior in feature-specific command classes.
- Add packaged-tool end-to-end tests that install a local nupkg and execute the full workflow.
- Complete semantic descriptions, examples, deprecations, and future migration rules in both schemas.

Exit: a new user reaches a compiling managed consumer on Windows, macOS, and Linux without manually repairing generated configuration.

### Phase 2 — Complete IR-native architecture migration

Status: complete. `CSharpEmitter` consumes only IR and emits raw ABI plus string/span/ref/out friendly surfaces. The compatibility emitter and old-schema migration path were deleted. Real-library and reviewed API snapshots remain release gates.

- Frontends only parse; analysis produces the single canonical `BindingModule`.
- C#, Runtime, C Bridge, inspection, and future emitters consume only IR plus an emission context.
- Keep C# public output semantics exclusively in IR-native type/function/marshalling emitters.
- Preserve behavior with public-API snapshots and generated-source equivalence.
- Enforce that emitters do not revisit AST and Intermediate references no parser/runtime/tool layer.

Exit: primary C# configured generation has no compatibility emission and real-library/API snapshots remain stable.

### Phase 3 — Complete C ABI coverage

- Cover nested declarators, function pointers, arrays, anonymous types, and flexible array members.
- Cover packing/alignment, unions, bitfields, zero-width bitfields, enums, and target-dependent primitives.
- Expand macro expressions, conditional declarations, and compiler-extension diagnostics.
- Model callback calling convention, user data, retention, unregister, and threading contracts.
- Keep variadics limited to explicitly configured, promoted signature variants.
- Require MSVC, GNU, and Darwin layout/invocation gates.

Exit: real C library failures are explicit unsupported contracts, never silent mis-generation.

### Phase 4 — C++ semantic bridge and final lowering SPI

Status: complete for the declared desktop subset. Built-ins, `TypeLowerings` / `CallableLowerings`, typed `ICppTypeLowering` / `ICppCallableLowering` / `ICppArtifactContributor` plugins, and explicit `NativeShims` use one deterministic registry. The superseded pre-release adapter SPI was deleted without a compatibility layer.

- Complete callable qualifiers, overloads, operators, namespaces, constructors/destructors, and exception boundaries.
- Model inheritance, virtual/non-virtual bases, pointer adjustment, and RTTI availability.
- Keep template generation explicit; full and partial specialization selection has native compile tests.
- Preserve the current lowering contract through reviewed API gates after the first stable release.
- Maintain declared ABI, ownership, and invalidation rules for every built-in lowering.
- Maintain callback lifetime tokens, threading policy, exception translation, and dispose-race tests.

Exit: a new project lowering changes neither parser nor unrelated emitters; unknown specializations fail deterministically unless an explicit `AllowUnsafe` policy accepts an auditable recipe/plugin/shim.

### Phase 5 — Ownership, marshalling, and safety contracts

Status: complete for the declared contracts. IR models allocator domains/pairs, callback retention/threading/unregister, and async completion; Runtime primitives test unregister/dispose races and exactly-once async terminal cleanup.

- Unify borrowed, owned, transferred, shared, pinned, and caller-allocated lifetimes.
- Type allocator/deallocator pairs, arenas, contexts, encoding, nullability, length, capacity, and written counts.
- Compose callback retention, threading, async completion, and unregister policies.
- Generate SafeHandle/IDisposable/Span/string APIs while retaining auditable raw ABI.
- Make `StrictSafetySeverity=Error` the recommended release/CI mode.

Exit: every friendly API that allocates, retains a pointer, or stores a callback exposes the complete lifetime source in IR.

### Phase 6 — Native build and artifact orchestration

Status: core providers and multi-RID layout are complete. After export verification, `native-build --package-root` writes `runtimes/<rid>/native/` and a SHA-256 index.

- Extend the existing bridge manifest with verified export inspection and provider results.
- Keep direct, CMake, Meson, clang-cl, and MSBuild plans equivalent as manifest fields evolve; add Ninja as a selectable CMake/Meson executor where it materially improves builds.
- Continue multi-configuration and loader validation work; desktop x64/arm64 RID mapping is implemented.
- Extend `native-build` with export verification and multi-configuration provider selection; original-library dependencies stay declarative inputs.

Exit: the C++ demo and bimg bridge produce native artifacts from configuration plus a standard provider.

### Phase 7 — Real cross-platform matrix

Status: implementation complete, same-version host evidence pending. CI uses Windows x64, Linux x64, and Intel macOS runners; no job is counted until it emits its own report.

- Tier 1: Windows x64/arm64 with MSVC and clang-cl; Linux x64/arm64 with GCC/Clang; macOS arm64/x64.
- Tier 2: Android, iOS, and FreeBSD. All are formal support targets; each requires explicit toolchains/sysroots, target-specific package layout, and independent device/simulator reports, and remains ⚠️ until complete.
- Run managed, real-library, native ABI/runtime, package-consumer, and target-snapshot gates per target.
- Cross targets that cannot run receive compile/link plus artifact inspection, never a runtime-pass label.

Exit: every README “production supported” platform has a current independent acceptance report.

### Phase 8 — Performance, caching, and scale

Status: the first release gate is complete. C and C++ configured generation use a content-addressed immutable cache with atomic restore/publication and concurrent-writer tests; a 10,000-declaration cold/warm budget is part of full acceptance. Workspace DAG scheduling, memory trends, and shared parser caches remain.

- Maintain declaration, configuration, compiler/toolchain, plugin, lowering, and shim fingerprints as versioned cache inputs.
- Add workspace DAG execution, parallel projects, shared parser caches, and isolated transactions.
- Gate cold/warm time, peak memory, output size, and deterministic diff on real libraries.
- Stress 10k+ declarations, multiple translation units, and large explicit template sets.

Exit: an unchanged workspace meets its warm-generation budget and cache entries never cross target/ABI boundaries.

### Phase 9 — Stable extension ecosystem

Status: pre-release API gate implemented. No legacy support or obsolete window is active before the first stable release; the architecture is ready to start that lifecycle afterward.

- Version IR, diagnostics, lowering, emitter, and build-provider public contracts separately.
- Maintain reviewed API baselines now; add obsolete windows and configuration migration only after the first stable contract exists.
- Provide thin Roslyn source-generator/MSBuild integrations while keeping generation host-independent.
- Enforce plugin version checks, isolated diagnostics, and deterministic ordering.

Exit: third-party extensions do not reference parser internals and minor releases preserve published contracts.

### Phase 10 — Release and long-term maintenance

- Deterministic packages, SPDX SBOM, SLSA provenance, license inventory, vulnerability gate, and the GitHub OIDC attestation workflow are implemented. Actual signatures are accepted only from an authorized release run.
- Version schemas, migrate configs, publish compatibility tables, and provide minimal reproduction templates.
- Retain all target acceptance artifacts and performance trends for every release.

Exit: a tag reproduces the release and consumers can determine config/package/target compatibility.

## Execution order

1. Finish Phase 1 to reduce the cost of every later test and adoption path.
2. Evolve Phases 2 and 3 together; every new ABI capability enters IR first.
3. Extend new STL types only through the Phase 4 lowering and Phase 5 safety contracts.
4. Complete Phase 6 before expanding the Phase 7 platform matrix.
5. Treat Phases 8, 9, and 10 as continuous release gates, including an actual OIDC-signed release execution.

## Definition of Done for every change

- no native-library-name or path special case;
- public behavior, negative behavior, and diagnostics have tests;
- ABI/lifetime behavior has native compilation or invocation;
- platform differences use target abstractions, not scattered host conditionals;
- docs, schemas, samples, and package README stay synchronized;
- warnings-as-errors solution build, managed tests, relevant native gate, and deterministic diff pass.
