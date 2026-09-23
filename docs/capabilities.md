# Capabilities and Boundaries

[简体中文](capabilities.cn.md) | [Documentation index](README.md) | [Acceptance specification](acceptance.md)

This document answers two questions: what BindGen-CS can reliably do today, and which capabilities still require explicit project semantics or more target evidence. It describes the current implementation rather than a roadmap disguised as a feature list.

## Evidence levels

| Level | Meaning |
| --- | --- |
| Host acceptance | Exercised on the declared target with real headers, generated-code compilation, native invocation, or integration tests |
| Automated test | Covered by unit/integration/generated-code tests, without implying full real-library acceptance on every host |
| Configuration support | The model and diagnostics exist, but the project must supply native semantics |
| Explicit rejection | Generation fails when safe lowering cannot be established |

## C and ABI

| Capability | Status | Evidence or boundary |
| --- | --- | --- |
| Functions, enums, constants, typedefs | Host acceptance | Five real C APIs through deterministic snapshots and warning-free IR-native compilation |
| Structs, unions, packing, fixed arrays, bitfields | Host acceptance / automated test | Layout and native invocation gates |
| Opaque handles, pointer typedefs, forward declarations | Host acceptance / automated test | SDL3, bgfx, cimgui, and compilation matrix |
| Callbacks, function pointers, callback registry | Host acceptance / automated test | C ABI callback and Runtime tests |
| C variadics | Configuration support | Fixed variants must use promoted argument types; currently limited to `DllImport` |
| Target-dependent `char`, `long`, `wchar_t`, `long double`, `va_list` | Host acceptance / automated test | MSVC/GNU/Darwin mappings; Darwin and GNU Arm64 reports, including the AAPCS64 `va_list` carrier |
| `DllImport`, `LibraryImport`, FunctionTable | Automated test | Generated compile/runtime coverage for configured import modes and native contexts |

## Friendly APIs and safety

| Capability | Status | Evidence or boundary |
| --- | --- | --- |
| Naming, type, field, and function mappings | Host acceptance / automated test | Configuration entry tests and real API snapshots |
| String encoding and ownership | Configuration support | Use `MarshallingMappings` when declarations do not express lifetime |
| Pointer/count, capacity/written-count, Span | Configuration support / automated test | Conservative inference plus explicit mapping; strict mode can remove unsafe friendly overloads |
| Cleanup functions and owned returns | Configuration support | Allocator and cleanup contracts must come from project configuration |
| Callback lifetime | Configuration support | Missing lifetime emits `BGCS-SAFETY-CALLBACK` |
| Generated API stability | Host acceptance | Deterministic source hashes and reflection public-API snapshots |

## C++ bridge

| Capability | Status | Evidence or boundary |
| --- | --- | --- |
| Classes, construction, destruction, instance/static methods | Host acceptance / automated test | Native bridge compilation and invocation gates |
| Overloads, namespace functions, exception boundary | Host acceptance / automated test | Generated symbols and exception-channel tests |
| Inheritance casts and pointer adjustment | Automated test | Avoids unsafe plain reinterpret casts |
| Class/function templates | Configuration support / automated test | Only instances listed in `TemplateInstantiations` / `FunctionTemplateInstantiations` are emitted |
| `std::string` | Automated test | Verified UTF-8 borrowed/return lowering scope |
| `std::vector`, `std::span` | Automated test | Pointer/count views; ownership still comes from configuration |
| `std::optional<T>` | Automated test | Both blittable presence/value and non-blittable owned-handle protocols have native compile tests |
| `std::array`, `std::map`, `std::set` | Native invocation test | Fixed extent and owned-holder protocols compile and execute |
| `std::variant`, `std::expected` | Native invocation test | Alternative/value/error holders compile, invoke, and destroy |
| `std::filesystem::path`, `std::chrono` | Native invocation test | UTF-8 path and nanosecond duration/time-point conversion execute natively |
| `std::unique_ptr`, `std::shared_ptr` | Automated test | Supported ownership-transfer/retention paths |
| Pure-virtual managed callback proxy | Automated test | Interfaces must be listed in `VirtualCallbackInterfaces` |
| Arbitrary STL/container/template metaprogramming | Explicit rejection | Unknown specializations emit `BGCSCPP001` instead of pretending to be blittable |

## Workflow and engineering

| Capability | Status | Notes |
| --- | --- | --- |
| `init → doctor → validate → generate → build` | Host acceptance | Covered by tool-install and clean-consumer smoke tests |
| Transactional output | Automated test | Failure preserves the last-good output |
| Deterministic `diff` | Host acceptance | Real-library and isolated output gates |
| Multi-project workspaces | Host acceptance | Workspace validation/generation/diff; InnoEngine five-project clean regeneration and native/build/test gate pass on macOS Arm64 |
| C++ native build manifest/providers | Native invocation + plan tests | Direct Clang/GNU and CMake host execution; Windows clang-cl/MSBuild tests build, inspect, and invoke DLLs on the owning runner; Meson plan coverage |
| Incremental generation | Automated + performance test | SHA-256 input/config/compiler/plugin/lowering/shim fingerprint, atomic immutable entries, concurrent publication, deleted-output restoration, 10k declaration cold/warm budgets |
| Final lowering extension contract | Native invocation + external-assembly E2E + API-shape test | Isolated plugin loader, deterministic typed services, declarative recipe, explicit shim, managed/native artifact, safety bypass diagnostic, and stable cache fingerprint |
| BaseConfig and presets | Automated test | Explicit config-directory context, cycle detection, override precedence, no process-CWD mutation |
| Installed-version C/C++ schemas | Automated test | Strict root properties by default; nested public shapes and core descriptions are generated from the installed types |
| Portable project initialization | Automated test | Config-relative `/` paths, explicit C/C++ selection, non-overwrite behavior |
| Deterministic NuGet packages | Host acceptance | Two-pack content comparison, clean restore, tool install |
| IR-native raw ABI backend | Host acceptance | Five real C libraries generated and compiled with warnings as errors; unsupported/opaque by-value semantics fail before commit |

## Target evidence

Status: completely verified ✅; formal support target with implementation or host acceptance pending ⚠️.

| Target | Status | Current evidence |
| --- | :---: | --- |
| macOS arm64 Darwin | ✅ | Complete current-source report passed; all ten mandatory categories score 9.0/10 |
| Windows x64 MSVC/clang-cl | ⚠️ | Dedicated runner and real clang-cl/MSBuild tests configured; complete report pending |
| Linux x64 GNU/Clang | ⚠️ | Dedicated runner configured; complete report pending |
| macOS x64 Darwin | ⚠️ | Dedicated Intel runner configured; complete report pending |
| Windows/Linux arm64 | ⚠️ | Target and desktop RID model exist; complete provider/runtime/package reports remain |
| Android | ⚠️ | Formal support target; target model exists, while NDK/sysroot, package layout, and device/emulator reports remain |
| iOS | ⚠️ | Formal support target; target model exists, while Xcode SDK, framework layout, and device/simulator reports remain |
| FreeBSD | ⚠️ | Formal support target; target model exists, while toolchain, package layout, and an independent report remain |

## Maturity assessment

BindGen-CS is already a strong, engineered binding toolkit rather than a thin header-to-`DllImport` script. On its accepted target it has production-grade C binding, a substantial controlled C++ bridge, reproducible output, package verification, and large-project integration evidence.

It cannot honestly claim automatic coverage of every C++ program or production verification on every modeled target. The main gaps are:

- Windows and x64 hosts do not yet have equivalent target-specific acceptance artifacts;
- the configuration model is powerful but still broad and flat for large libraries;
- non-host execution evidence is still required for clang-cl/MSBuild; multi-RID desktop packaging is implemented but Windows runtime execution remains unverified;
- arbitrary metaprogramming, custom allocators, and types beyond built-in protocols require a declarative lowering, versioned plugin, or explicit C ABI shim.

The accurate position is: **excellent within the accepted C ABI and explicitly supported C++ subset; not yet a zero-configuration universal translator for arbitrary C++ on every platform.**

## Highest-value next steps

1. Produce equivalent real-library, native-invocation, and package reports on Windows x64, Linux x64, and macOS x64.
2. Execute every provider on its owning target and retain the multi-RID consumer evidence.
3. Preserve and repeat the passing InnoEngine clean-regeneration/import-audit/native-build/full-solution/native-test gate on each adopted target.
4. Execute a real OIDC/Sigstore signed release in an authorized GitHub release environment.
5. Continue built-in semantic/schema coverage while routing project-specific semantics through the final lowering architecture.
