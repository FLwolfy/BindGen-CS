# Pre-release compatibility and future deprecation policy

[简体中文](compatibility-policy.cn.md) | [Documentation](README.md)

BindGen-CS has not published its first stable release. Therefore the current product deliberately makes **no backward-compatibility promise** for older pre-release configurations, emitters, generated source, or public APIs.

## Policy before the first stable release

- Exactly one `ConfigVersion` is accepted. A different version fails validation with a stable diagnostic; there is no implicit migration or fallback emitter.
- Canonical Binding IR is the only C# emission source. Removed prototype/AST output paths are not retained behind compatibility flags.
- Public API snapshots are a review gate, not a promise that pre-release APIs cannot change. An intentional change updates the baseline in the same reviewed change.
- ABI and memory safety take priority. For C bindings, unknown ownership, allocator, callback, or async semantics suppress inferred friendly overloads by default while retaining raw ABI and diagnostics; `StrictSafetySeverity=Error` rejects the entire output. Unknown C++ inheritance/template lowerings are rejected unless an explicit extension handles them.
- Platform support exists only when the same source revision has a target-specific acceptance report. A plan, cross-compile, or report from another architecture is not evidence.

The architecture keeps explicit configuration versions, typed diagnostics, a plugin revision handshake, and API-diff automation so compatibility can be introduced cleanly later. There is one current lowering contract; it does not load or wrap the deleted pre-release adapter SPI. The revision handshake is an implementation guard, not a public “v1/v2” product label.

## Policy starting with the first stable release

The first stable release establishes the initial compatibility baseline. After that point, removing a public contract requires:

1. announcement, changelog entry, replacement, and machine-readable diagnostic;
2. compile-time/CLI/schema deprecation for at least two minor releases;
3. an explicit config migration command when configuration syntax changes;
4. removal only in the next major, normally no sooner than 12 months after announcement;
5. API diff, migration tests, clean consumers, and target acceptance for the removal.

Security vulnerabilities, demonstrated ABI corruption, or mandatory upstream-runtime removal may shorten the cycle, with evidence and mitigation documented in release notes.

There is currently no deprecation register because there is no stable legacy contract.

## Release evidence

`bindgen-cs supply-chain` emits deterministic SPDX 2.3 SBOM and SLSA v1 provenance payloads containing artifact SHA-256 values, source revision, builder identity, and build parameters. The release workflow is configured to sign package provenance and the SBOM association through GitHub OIDC/Sigstore attestations. A signature is valid evidence only when an authorized release job actually obtains the OIDC identity and publishes verifiable attestations; a local run cannot satisfy or simulate that condition. Release candidates must also pass public API, dependency license, vulnerability, deterministic package, clean native-RID consumer, and complete desktop acceptance gates.
