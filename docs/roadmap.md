# BGCS 3.0 implementation and certification roadmap

[中文](roadmap.cn.md) | [Architecture](architecture.md) | [Acceptance](acceptance.md)

BGCS 3.0 separates source facts, native ABI and semantics, C++ bridge operations, managed projection, C# emission, native construction, Runtime and integration. Its goal is a small user-facing product surface with independently replaceable implementation modules. No old generator API, flat configuration reader, migration tool or compatibility wrapper is retained. The baseline is .NET 10/C# 14, C17 and C++23.

Source implementation, reviewed correspondence and release certification are separate states. The [implementation status](implementation-status.cn.md) records local development evidence. The [release evidence rules](release-evidence-state-flow.md) define how a clean commit obtains platform, package and publication proof. A successful source build or a directory reorganization does not finish this roadmap.

| Phase | Implementation and required exit evidence |
| --- | --- |
| 0 — Baseline | Thirteen production projects; exact dependency policy; current feature/configuration inventory; deterministic net10 builds and locked toolchains. |
| 1 — Contracts | Detached immutable native, bridge, interop and managed models; versioned serialization; Contracts-only extension compilation; no parser handles or outer-layer dependencies. |
| 2 — Targets and worker | Separate host parser, target ABI, SDK and managed profile; checked parser resources; cancellation, timeout, malformed protocol and actual native crash isolation. |
| 3 — Analysis | Declaration closure, independent storage and calling-ABI checks, evidence-aware ownership/text/buffer/callback/async/error semantics; raw access and friendly eligibility remain distinct. |
| 4 — C++ bridge | Typed operations, configured and standard family lowerings, templates, inheritance casts, exception boundaries, virtual proxies and shims; reparse exported C ABI and merge semantic contracts. |
| 5 — C# backend | Shared projection and emission plans for all three imports; instance/static styles, raw/friendly surfaces, deterministic naming, split/single output, source processors, metadata and final contract checks. |
| 6 — Runtime | Module/resource/call leases; owned/borrowed objects; callback and async shutdown; strict codecs and matching allocators; package/source parity and explicitly bounded special storage. |
| 7 — Host | One pipeline for CLI/SDK/workspace/MSBuild; plugin bootstrap and freezing; complete cache inputs and artifact sets; candidate validation and recoverable publication. |
| 8 — Integration and distribution | Native providers, exact binary/export/assets checks, opt-in independent MSBuild TaskHost; four products with complete parser execution closures and clean installed consumers. |
| 9 — First release | Same-revision Windows x64, Linux GNU x64, macOS Intel, browser interpreter/AOT and NativeAOT; locked real libraries, fault injection, performance, public API, licenses and supply-chain proof. |
| 10 — Further certification | ARM64, musl, Android, iOS device/simulators and FreeBSD each obtain their own parse/generate/compile/link/invoke/package evidence. |

The public products are `BGCS`, `BGCS.Contracts`, `BGCS.Runtime` and `BindGen-CS`. Internal assemblies, parser runtime assets and host tool partitions are distribution details; users do not assemble a package for every source project.

Platform adapters express real CPU, ABI, SDK, module-loading and managed-runtime constraints. A target descriptor alone does not certify calls. Unsupported by-value layouts and missing lifecycle proof must fail or retain only a correct raw route; matching `sizeof` or a user assertion cannot become verified ABI evidence. Browser virtual shutdown that requires blocking is an explicit capability restriction, not an invented safe wrapper.

Release requires all frozen feature and configuration identities to have reviewed implementation and exact executable assertions. CI proves those declarations for one clean revision. The trusted release chain then verifies every mandatory job and installed archive, creates the immutable version tag, publishes NuGet, verifies public package bytes and performs clean public-source installation. Checked-in `releaseReady` stays false; only the separate successful release preflight can derive a release-ready result.

Maintenance begins after these gates pass. New lowerings should remain outside the frontend; new targets should not require edits throughout emitters; Runtime consumers should carry no generator. Toolchain or ABI changes rerun affected evidence. This is an enforceable maintenance boundary, not a promise that future C++ or execution platforms require no engineering.

The [archived 2.x roadmap](roadmap-2.x.md) preserves earlier design history and is not the 3.0 usage or completion contract.
