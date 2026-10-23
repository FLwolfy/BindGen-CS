# BGCS diagnostic reference

<!-- Generated from eng/diagnostics/catalog.json; edit that source and regenerate. -->

`bindgen-cs explain CODE` exposes the same catalog's English cause and resolution. The individual message, source location and stage provide the current failure context.

## BGCS-ANALYSIS-INPUTS — Analysis inputs could not be released

Cause: Capturing or releasing the frozen analysis inputs failed.

Resolution: Inspect the detailed file or plugin failure; close conflicting handles and repair the input declaration before creating a new request.

## BGCS-ANALYSIS-SESSION — Native analysis session failed

Cause: A target, frontend, native graph or analysis extension failed before a valid model was returned.

Resolution: Use the accompanying stage, target and source diagnostic to fix the parser arguments, target prerequisites or failing extension; rerun inspect.

## BGCS-C-ABI — C declaration has no supported direct ABI

Cause: A C declaration lacks an exact fixed prototype or contains an unsupported direct ABI type; a C++ graph cannot enter direct C analysis.

Resolution: Declare exact C prototypes and supported ABI carriers; lower C++ through its bridge or add an explicit native shim for unsupported signatures.

## BGCS-C-INTERNAL-LINKAGE — Configured native module is missing C header helper exports

Cause: A selected C function has internal linkage, a header definition, or compiler-established direct-call-only intrinsic semantics instead of an independently addressable external symbol. A prebuilt module cannot supply a generated helper automatically.

Resolution: Build the emitted C helper sources into the configured native module so their explicit exported wrappers exist, or provide a declared native shim. A helper-only dependency DLL does not supply the parent module exports on Windows.

## BGCS-CACHE-BUDGET — Generated cache entry exceeds its budget

Cause: A successful generated stage is too large for the configured serialized cache-entry limit.

Resolution: Inspect artifact size; raise cache.maximumEntryBytes within its supported bound or keep the successful result uncached.

## BGCS-CACHE-MISS — Cached stage was not reused

Cause: A stored stage is missing, corrupt, stale or incompatible with the current complete contract check.

Resolution: Read the reported reason; normal regeneration repairs a stale or corrupt entry. Do not disable current ABI checks to accept old bytes.

## BGCS-CACHE-STORE — Published result could not be cached

Cause: Generation published successfully, but writing a reusable immutable cache entry failed.

Resolution: Check cache directory permissions, space and competing handles; the published result remains the successful output.

## BGCS-CACHE-UNAVAILABLE — Generation cache is unavailable

Cause: Preparing, identifying or reading an eligible generation cache failed.

Resolution: Inspect the cache failure, choose a writable nonoverlapping cache directory, or disable caching while retaining ordinary generation and validation.

## BGCS-CALL-ABI — Physical calling ABI is unsupported

Cause: The selected ABI provider cannot establish a safe forward or reverse call for the final physical signature. The diagnostic retains the exact rejected native type location when supplied.

Resolution: Use a supported pointer bridge or explicit native shim, or supply an applicable ABI implementation with an exact call plan. Do not substitute equal-sized storage for calling compatibility.

## BGCS-CONSTANT-OMITTED — Object macro has no supported scalar value

Cause: The compiler did not establish a supported scalar constant for an object macro; its original replacement remains in native facts.

Resolution: Use a native/interop transform for the intended non-scalar semantics, or leave the macro unprojected. Text, type aliases and context-dependent macros are not guessed as numeric constants.

## BGCS-CSHARP-ABI-PLAN — C# ABI plan cannot represent the native declaration

Cause: The native declaration has no proven C# storage or by-value calling representation for its resolved ABI, managed runtime and import mode; the attached source location identifies the declaration or field.

Resolution: Inspect the detailed type, layout or calling restriction. Use an explicit pointer-based native bridge or shim, or select an independently verified compatible runtime/import profile; do not replace the native type based only on equal size.

## BGCS-DIFF-METADATA — Generated metadata contains logical field changes

Cause: Diff found non-cache metadata changes and reports a bounded sample of their JSON paths and previous/current values.

Resolution: Review the reported fields against the intended input or configuration change. Unchanged requests should preserve logical verification evidence; do not ignore changed ABI or input records merely because generated source text is unchanged.

## BGCS-DIFF-OUTPUT — Existing output cannot be compared safely

Cause: Diff could not read or verify the previous owned publication before comparing it with candidate generation.

Resolution: Check the prior ownership manifest, artifact hashes, paths and file access; restore a complete owned output before retrying diff.

## BGCS-DOCTOR-CONFIGURATION — Configuration prerequisites are invalid

Cause: Doctor could not load or validate the grouped configuration and its declared plugins.

Resolution: Fix the reported schema, path, composition or plugin error before checking compiler and worker prerequisites.

## BGCS-DOCTOR-TARGET — Native target cannot be resolved

Cause: The requested target, SDK, sysroot or toolchain cannot be resolved from the explicit configuration and installed environment.

Resolution: Correct the target identity and SDK/toolchain paths or register the intended target provider before retrying doctor.

## BGCS-DOCTOR-WORKER — Parser worker assets are invalid

Cause: The host-specific worker execution closure is missing, incomplete or fails its declared digest checks.

Resolution: Redeploy the complete matching worker assets for the generator host; do not select parser assets by the generated native target.

## BGCS-FRIENDLY-OMITTED — Friendly projection lacks a complete contract

Cause: A requested text or buffer wrapper cannot prove its encoding, extent, direction, retention, lifetime or alignment contract.

Resolution: Complete the exact semantics.stringParameters or semantics.bufferParameters assertion when justified; otherwise use the valid raw ABI without an unsafe friendly wrapper.

## BGCS-FRONTEND-CLANG — Clang reported a source diagnostic

Cause: The parser worker forwarded a Clang diagnostic for the current language, target and effective source arguments.

Resolution: Use the original source location and compiler message to fix declarations, macros, include directories or target SDK inputs.

## BGCS-GENERATION-SESSION — Generation session could not complete

Cause: A required generation, lowering, ABI planning, plugin, validation or publication stage failed.

Resolution: Follow the detailed failure and source/target context; add a supported neutral lowering or native shim when the ABI cannot be expressed. Previous complete output is not replaced on failure.

## BGCS-MACRO-ALIAS — Function macro alias has no unambiguous checked export

Cause: A compiler-established object-like macro names a source free function, but the final interoperability model has zero or multiple eligible primary exports for that exact callable. Input adapters are not primary exports.

Resolution: Preserve the exact original callable through source selection and lowering, or provide an explicit unambiguous managed projection. BGCS will not invent an export or guess an overload for the macro alias.

## BGCS-NATIVE-GRAPH — Native fact graph is inconsistent

Cause: Detached native declarations or types contain missing, duplicate, cyclic or incompatible graph facts.

Resolution: Fix the frontend or native transform that produced the identified facts; do not continue generation with substituted default type/layout values.

## BGCS-PUBLICATION-CLEANUP — Committed publication backup cleanup failed

Cause: The complete candidate was published, but obsolete owned backup or journal cleanup failed.

Resolution: Check the named owned path and remaining handles/space; retain the valid current manifest and use the publication recovery protocol for leftovers.

## BGCS-SAFETY-ALLOCATOR — Owned output has no matching allocator

Cause: An owned native value lacks a complete allocation/release-domain contract.

Resolution: Declare the justified release function and domain through semantics.ownedResults or a neutral interop contract; retain a raw pointer when cleanup cannot be established.

## BGCS-SAFETY-ASYNC — Native completion lifetime is incomplete

Cause: Asynchronous native access has no established terminal completion boundary for retained callbacks or buffers.

Resolution: Declare a real terminal completion protocol in the neutral async contract; keep pins, callbacks and module leases until native access has actually ended.

## BGCS-SAFETY-CALLBACK — Callback lifetime is incomplete

Cause: A callback contract does not establish native retention, unregister completion and in-flight invocation lifetime.

Resolution: Supply an explicit neutral callback contract with the library protocol, or manage the raw callback lifetime yourself until native access has ended.

## BGCS-SAFETY-CALLBACK-THREAD — Callback thread contract is incomplete

Cause: The allowed native callback threads or thread-affine release domain are unknown.

Resolution: Declare the permitted callback/release threads in the neutral semantic contract and synchronize application state for those threads.

## BGCS-SAFETY-EXTERNAL-TYPE — External carrier needs independent ABI evidence

Cause: A supplied managed type is used across the native boundary without complete matching storage and calling-ABI evidence.

Resolution: Provide independently validated target layout and calling compatibility or use a native pointer/bridge representation; a type-name or equal-size assertion is insufficient.

## BGCS-SAFETY-LENGTH — Native buffer bounds are incomplete

Cause: The selected buffer lacks a proven length, capacity or written-count relationship.

Resolution: Declare the exact pointer and lengthParameter relationship in semantics.bufferParameters when supported, or provide an explicit interop contract/native adapter for richer protocols.

## BGCS-SAFETY-LOWERING-BYPASS — Unsafe lowering was explicitly selected

Cause: A lowering was accepted under an explicit unsafe policy rather than complete ABI/lifetime proof.

Resolution: Retain independent native invocation and lifetime tests, and replace the bypass with a declared and validated neutral lowering where available.

## BGCS-SAFETY-OWNERSHIP — Native pointer ownership is incomplete

Cause: A returned data address lacks explicit ownership or pointer-validity facts with an identified evidence source; its raw ABI does not establish its lifetime.

Resolution: Declare matching release semantics through semantics.ownedResults, explicit module validity through semantics.borrowedResults, or a neutral plugin contract. Select this warning in execution.diagnostics.errorCodes to block publication; do not infer ownership or encoding from names.

## BGCS-SESSION-RELEASE — Generation service release failed

Cause: A session-owned service or plugin failed during disposal.

Resolution: Fix cleanup in the named extension and release its resources; rerun the request so lifecycle failure is included in the complete result.

## BGCS-SOURCE-CLEANUP — Published generation left private source storage to clean

Cause: The complete candidate was already committed, but cleanup of a host-owned private contributed-source store failed.

Resolution: Inspect the exact private-store path and maintenance error, release conflicting file handles or attributes, and retry its verified owned cleanup. The published artifact set remains valid; contributor-service disposal errors before commit still block publication.

## BGCS-WORKSPACE-DEPENDENCY — Workspace prerequisites failed

Cause: A workspace project was skipped because one or more explicit prerequisite projects failed.

Resolution: Repair and rerun the named prerequisite projects; dependent output is not published from failed upstream work.

## BGCS-WORKSPACE-PROJECT — Workspace project failed

Cause: One project request failed while the workspace executed its dependency graph.

Resolution: Inspect that project configuration and its generation/build diagnostics; independent projects may finish, but the workspace remains failed.

## BGCSCPP-INSTANTIATION — C++ template needs a concrete instance

Cause: A selected primary class or function template has no explicit concrete specialization.

Resolution: Declare the requested concrete class/function instance in bridge.cpp.templateInstantiations and provide all required type/value arguments.

## BGCSCPP001 — C++ declaration has no safe lowering

Cause: No registered C++ lowering preserves the selected declaration and its ABI/lifetime contract.

Resolution: Add a typed Contracts-only lowering, a configured recipe, a concrete template instance or an explicit native shim.

## BGCSCS001 — Managed backend cannot preserve semantics

Cause: The managed model requests a type or callable the selected C# backend cannot express without losing its contract.

Resolution: Use a supported neutral projection/lowering or native shim, or implement and validate the missing backend capability before generation.

## BGCSIO001 — Artifact publication failed

Cause: A file-system operation failed while staging or committing generated artifacts.

Resolution: Check the exact path, permissions, available space and open handles. Failed publication preserves prior output; use publication recovery for an interrupted commit.
