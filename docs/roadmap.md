# Universal BindGen Execution Roadmap

[中文](roadmap.cn.md) | [Capability matrix](capabilities.md) | [Acceptance specification](acceptance.md)

## End state

“Universal” does not mean guessing unknown C++ semantics. BindGen-CS is complete when it generates correct code under explicit target, ABI, ownership, and build contracts; general adapters extend supported types; and declarations that cannot be proven safe fail with stable, actionable diagnostics.

Every dimension below must score at least 9.0/10.0. An average score cannot hide a failed dimension.

| Dimension | 9.0 acceptance condition |
| --- | --- |
| C ABI completeness | Common declaration, layout, callback, and variadic patterns have native compile/layout/invocation evidence |
| C++ bridge | Every declared class/template/STL/ownership feature has native invocation evidence; unknown semantics are rejected |
| Managed API quality | Raw ABI is correct and every friendly API has auditable ownership, encoding, length, and cleanup contracts |
| Usability | A new project completes `init → doctor → validate → generate → build`; failures identify an exact config repair path |
| Portability | Every production target has its own acceptance artifact; model support is not counted as host verification |
| Architecture | Parser, analysis, IR, emitters, Runtime, CLI, and build providers have one-way dependencies enforced by tests |
| Extensibility | A type adapter, emitter, or build provider can be added without unrelated core changes or library-name special cases |
| Performance | Cold/warm budgets, cache correctness, deterministic hashes, and concurrency safety are release gates |
| Release supply chain | Deterministic packages, SBOM/provenance, compatibility policy, and clean consumers pass |
| InnoEngine | Every native binding is config/generated, handwritten imports are rejected, and the full native/build/test gate passes |

## Non-negotiable rules

1. Core code never branches on a native library name.
2. Library semantics enter through general configuration, adapter/provider contracts, or reusable analysis rules.
3. Correct raw ABI takes priority over the number of friendly overloads; uncertain lifetime retains raw access and emits a diagnostic.
4. Target support binds compiler, triple, sysroot, ABI, and real compile/run evidence.
5. Generated output is transactional; customization lives in configuration, plugins, or separate partial files.
6. New features need positive and negative coverage plus snapshot or compile coverage; ABI features require a native test.

## Phases

### Phase 0 — Measurable baseline

Status: complete. Ten acceptance categories, real-library gates, InnoEngine integration, package smoke tests, and evidence-level documentation are in place.

Exit: every capability claim links to a test/artifact or an explicit boundary.

### Phase 1 — Frictionless CLI and configuration contract

Status: in progress. This iteration completed portable `init`, explicit language selection, strict C/C++ schemas, configuration contract version 1, a versioned diagnostic catalog with `explain`, a dedicated Tool test project, explicit C++ configuration-directory resolution, and BaseConfig cycle detection without process-CWD mutation.

- Keep command behavior in feature-specific command classes.
- Add packaged-tool end-to-end tests that install a local nupkg and execute the full workflow.
- Complete semantic descriptions, examples, deprecations, and future migration rules in both schemas.

Exit: a new user reaches a compiling managed consumer on Windows, macOS, and Linux without manually repairing generated configuration.

### Phase 2 — Complete IR-native architecture migration

Status: in progress and still the highest architectural priority. `CSharpEmitter` is now strictly IR-only and `EmitLegacy` has been removed; the default compatibility surface is isolated in `AstGenerationStepEmitter`, which still must be eliminated without snapshot loss.

- Frontends only parse; analysis produces the single canonical `BindingModule`.
- C#, Runtime, C Bridge, inspection, and future emitters consume only IR plus an emission context.
- Move legacy `GenerationStep` behavior into IR-native type/function/marshalling emitters.
- Preserve behavior with public-API snapshots and generated-source equivalence.
- Remove `AstGenerationStepEmitter` from the default path after source/public-API equivalence, and time-box the compatibility adapter.
- Enforce that emitters do not revisit AST and Intermediate references no parser/runtime/tool layer.

Exit: primary C/C++ configured generation uses no legacy emission while real-library and InnoEngine snapshots remain stable.

### Phase 3 — Complete C ABI coverage

- Cover nested declarators, function pointers, arrays, anonymous types, and flexible array members.
- Cover packing/alignment, unions, bitfields, zero-width bitfields, enums, and target-dependent primitives.
- Expand macro expressions, conditional declarations, and compiler-extension diagnostics.
- Model callback calling convention, user data, retention, unregister, and threading contracts.
- Keep variadics limited to explicitly configured, promoted signature variants.
- Require MSVC, GNU, and Darwin layout/invocation gates.

Exit: real C library failures are explicit unsupported contracts, never silent mis-generation.

### Phase 4 — C++ semantic bridge and adapter SPI

Status: adapter SPI complete; broader C++ semantics remain. `ICppTypeAdapter` and `ICppCallableAdapter` are versioned and deterministic; built-in string/vector/span/optional/smart-pointer lowering uses the same registry, and free/static/instance/constructor/destructor callables share the naming/exclusion SPI.

- Complete callable qualifiers, overloads, operators, namespaces, constructors/destructors, and exception boundaries.
- Model inheritance, virtual/non-virtual bases, pointer adjustment, and RTTI availability.
- Keep template generation explicit and diagnose partial-specialization selection.
- Preserve the v1 `ICppTypeAdapter` / `ICppCallableAdapter` compatibility baseline while adding new general adapters.
- Add map/set/array/variant/expected/path/chrono only with declared ABI, ownership, and invalidation rules.
- Extend callback proxies with lifetime tokens, threading policy, exception translation, and dispose-race tests.

Exit: a new container adapter changes neither parser nor unrelated emitters; unknown specializations fail deterministically.

### Phase 5 — Ownership, marshalling, and safety contracts

- Unify borrowed, owned, transferred, shared, pinned, and caller-allocated lifetimes.
- Type allocator/deallocator pairs, arenas, contexts, encoding, nullability, length, capacity, and written counts.
- Compose callback retention, threading, async completion, and unregister policies.
- Generate SafeHandle/IDisposable/Span/string APIs while retaining auditable raw ABI.
- Make `StrictSafetySeverity=Error` the recommended release/CI mode.

Exit: every friendly API that allocates, retains a pointer, or stores a callback exposes the complete lifetime source in IR.

### Phase 6 — Native build and artifact orchestration

Status: core provider work complete. C++ bridge generation emits a deterministic target-specific manifest; direct Clang/GNU, clang-cl, CMake, Meson, and MSBuild pipelines are built in, and successful CLI builds verify declared exports with `nm`/`dumpbin`.

- Extend the existing bridge manifest with verified export inspection and provider results.
- Keep direct, CMake, Meson, clang-cl, and MSBuild plans equivalent as manifest fields evolve; add Ninja as a selectable CMake/Meson executor where it materially improves builds.
- Support multi-RID/architecture builds, runtime-asset layout, library naming, and loader validation.
- Extend `native-build` with export verification and multi-configuration provider selection; original-library dependencies stay declarative inputs.

Exit: the C++ demo and bimg bridge produce native artifacts from configuration plus a standard provider.

### Phase 7 — Real cross-platform matrix

Status: macOS arm64 complete; Windows and Linux still require equivalent reports.

- Tier 1: Windows x64/arm64 with MSVC and clang-cl; Linux x64/arm64 with GCC/Clang; macOS arm64/x64.
- Tier 2: Android arm64/x64, iOS device/simulator, and FreeBSD x64 using explicit toolchains/sysroots.
- Run managed, real-library, native ABI/runtime, package-consumer, and target-snapshot gates per target.
- Cross targets that cannot run receive compile/link plus artifact inspection, never a runtime-pass label.

Exit: every README “production supported” platform has a current independent acceptance report.

### Phase 8 — Performance, caching, and scale

Status: the first release gate is complete. C and C++ configured generation use a content-addressed immutable cache with atomic restore/publication and concurrent-writer tests; a 10,000-declaration cold/warm budget is part of full acceptance. Workspace DAG scheduling, memory trends, and shared parser caches remain.

- Maintain declaration, configuration, compiler/toolchain, plugin, and adapter fingerprints as versioned cache inputs.
- Add workspace DAG execution, parallel projects, shared parser caches, and isolated transactions.
- Gate cold/warm time, peak memory, output size, and deterministic diff on real libraries.
- Stress 10k+ declarations, multiple translation units, and large explicit template sets.

Exit: an unchanged workspace meets its warm-generation budget and cache entries never cross target/ABI boundaries.

### Phase 9 — Stable extension ecosystem

Status: contract version 1 is implemented for plugin entry points and deterministic typed services, including C++ type/callable adapters and additional IR emitters. Isolated dependency resolution, atomic registration, assembly/content fingerprints, and v1 public-shape locks are tested; a formal obsolete window remains.

- Version IR, diagnostics, adapter, emitter, and build-provider public contracts separately.
- Add API compatibility baselines, an obsolete window, and configuration migration.
- Provide thin Roslyn source-generator/MSBuild integrations while keeping generation host-independent.
- Enforce plugin version checks, isolated diagnostics, and deterministic ordering.

Exit: third-party extensions do not reference parser internals and minor releases preserve published contracts.

### Phase 10 — Fully automated InnoEngine migration

- Give every native dependency its own config; compose shared behavior through presets, base configs, and adapters.
- Reject handwritten imports and native layout mirrors outside `Generated/`.
- Make one workspace command validate, generate, diff, build native dependencies, build managed code, and run native tests.
- Delete old bindings, duplicate runtime code, and temporary patches; every surviving patch must be a tested general transformation.

Exit: deleting all generated directories from a clean checkout and running the pipeline fully rebuilds InnoEngine, with no InnoEngine name/path checks in BindGen core.

### Phase 11 — Release and long-term maintenance

- Produce deterministic packages, SBOM, provenance, license inventory, and vulnerability gates.
- Version schemas, migrate configs, publish compatibility tables, and provide minimal reproduction templates.
- Retain all target acceptance artifacts and performance trends for every release.

Exit: a tag reproduces the release and consumers can determine config/package/target compatibility.

## Execution order

1. Finish Phase 1 to reduce the cost of every later test and adoption path.
2. Evolve Phases 2 and 3 together; every new ABI capability enters IR first and does not expand legacy emission.
3. Establish Phases 4 and 5 before adding more STL types.
4. Complete Phase 6 before expanding the Phase 7 platform matrix.
5. Migrate an InnoEngine binding after each reusable capability, continuously executing Phase 10.
6. Treat Phases 8, 9, and 11 as continuous release gates.

## Definition of Done for every change

- no native-library-name or path special case;
- public behavior, negative behavior, and diagnostics have tests;
- ABI/lifetime behavior has native compilation or invocation;
- platform differences use target abstractions, not scattered host conditionals;
- docs, schemas, samples, and package README stay synchronized;
- warnings-as-errors solution build, managed tests, relevant native gate, and deterministic diff pass.
