# Current contracts and release evidence

[简体中文](compatibility-policy.cn.md) | [Documentation](README.md)

BindGen-CS develops one current configuration, lowering and generation contract. A source change updates its consumers, examples, schema, API snapshots and tests together. Removed APIs and obsolete configuration layouts are not retained as fallback paths.

## Configuration and public APIs

- Configuration uses the current property names. It has no configuration schema revision field, migration reader or alternate legacy emitter.
- C# output consumes Binding IR. C++ bridge output consumes Bridge IR. Analysis owns Clang AST access; emitters receive frozen generation facts.
- File composition preserves explicitly supplied JSON values, including values equal to defaults. Base references are resolved from their containing file and removed from the composed result.
- Public API snapshots identify changes for review. Updating a snapshot requires checking the actual library consumers and documentation; a snapshot alone does not prove correctness.
- The plugin contract revision handshake rejects assemblies built against a different extension ABI. It is a load-time integrity check, not a configuration migration mechanism.

## ABI, ownership and target support

Unknown allocator, callback, asynchronous or ownership semantics do not justify guessing a managed convenience API. C generation retains the raw ABI where it can represent it safely and reports the missing semantic information. `strictSafetySeverity` controls whether those diagnostics reject publication. Unsupported C++ lowering must fail unless an explicitly registered extension describes the transformation.

A target descriptor records the ABI and SDK inputs used for parsing. A successful parse or cross compilation does not establish runtime support. Support evidence must include actual native calls from an independent BGCS consumer built from the same source revision, with the import mode, managed runtime, toolchain and host recorded. Reports from a consuming engine are integration evidence for that engine and do not replace BGCS acceptance.

Generation and packaging prepare candidates before replacing complete outputs. Failure must retain the previous output. Related native and managed output roots use a common publication owner; participating readers must obey that ownership because a filesystem cannot rename multiple directories simultaneously.

## Release evidence

`bindgen-cs supply-chain` emits deterministic SPDX 2.3 SBOM and SLSA v1 provenance payloads with artifact SHA-256 values, source revision, builder identity and build parameters. Release automation can publish GitHub OIDC/Sigstore attestations. Only an authorized release job that actually obtains the identity and publishes verifiable attestations establishes a signed release; a local run does not.

Package acceptance additionally checks the public API, dependency licenses, vulnerabilities, deterministic package contents, clean consumers and the supported target invocation matrix. Unexecuted hosts or architectures remain explicitly unverified in the acceptance report.
