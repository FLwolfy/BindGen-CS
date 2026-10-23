# Architecture review

Review scope: current 4.x production architecture, actual composition path and repository-only consumer boundary. This is not a passing test receipt.

- Core and Runtime do not reference other BGCS projects.
- Engine and algorithms do not reference concrete host/target/frontend/optional implementations.
- BGCS alone supplies standard composition. CLI uses the same Engine; its parser resource inspection is an implementation integration, not AST analysis.
- Standard host shares Infrastructure PhysicalPathResolver under ADR0008; there is no reverse dependency or second resolver.
- Host services precede configuration/manifests. A plugin cannot replace its bootstrap path semantics.
- Descriptor/factory registration is shared, duplicate IDs fail and replacement is explicit.
- Native facts, typed bridge operations, final physical interop and managed projection remain distinct.
- Generated virtual owner identities are bridge-owned, never fabricated frontend facts. Original parse and bridge verification have distinct purposes.
- Call plans bind exact signatures, direction, target/triple, runtime digest and ABI identity.
- Generation service cleanup precedes final input verification/commit; host infrastructure survives through commit. Post-commit cleanup errors retain publication state.
- Runtime owns calls/resources independently and has no generation or platform SDK discovery dependencies.
- Product APIs do not compile, link, package or run consumer projects. Test harnesses stay outside package closure.
- No compatibility namespace, old configuration reader, IVT/private behavior reflection or source-link boundary bypass is used.

Verification locations: eng/architecture/project-dependencies.json; ArchitectureValidator; independent Engine tests; final-artifact reader; worker/path and publication fault tests; actual package closure verifier.

The coordinator remains a substantial control-flow component. Its review must follow ordering, cancellation and resource ownership, not file size or interface count. Future refactoring requires a concrete maintenance problem and must preserve these invariants.

The independent source review found two concrete remaining boundary defects and
their corrections: cache overlap now compares the actual physical input paths
through injected directory semantics, and callable assertion policy comes from
explicit target-owned facts rather than a triple switch inside NativeBridge.
Neither correction removes an assertion or grants an unproved ABI. Execution
and package acceptance are recorded in release-specific receipts, independently
of this source review. See [release history](releases/README.md).

The final review also checks derived-work reuse boundaries: parser-cache admission
can reuse its graph/input checks only for the exact instance admitted at the same
synchronous boundary; later extension callbacks and publication retain independent
input fences. A provider may reuse one immutable runtime-profile digest, but every
physical signature and carrier is reassessed. No full-pipeline cache, new lifecycle
system or consumer build facility was introduced for performance.

Single-file formatting occurs once after composition. Every input fragment still
receives full syntax validation, and the processed output still receives all
contract checks. Parallel syntax parsing is bounded to two workers for large
explicit arrays; lazy inputs retain sequential enumeration and early-error behavior.

The emission key hashes every serialized byte of the closed, ordered managed
contract graph directly. Free-form configuration and extension JSON retain
canonical property ordering. The key revision changes; no old-format reader,
partial model fingerprint or cached signature assessment is introduced.

Repository consumers choose Release explicitly rather than inheriting ambient
MSBuild configuration. Historical parser fixtures retain their exact byte hashes;
only those captured header literals are exempt from editor whitespace trimming.
Neither measure changes a product capability or relaxes a generated-code check.

The CI correction review confirms that prepared C# emission retains the original
emission body and its complete actual-source validation. The sealed preparation
owns one immutable checked plan and request; callers can substitute only the sink.
Custom backends retain their existing port and do not inherit the built-in cache.
Warm cache artifacts and final processor output still undergo current contract
checks. There is no mutable cross-session plan cache or unchecked plan entry point.

Inspection cleanup first verifies the captured logical-to-physical boundary, then
checks and removes its physical scratch leaf. A legitimate temporary-directory
parent alias is supported without accepting a redirected owned directory.

Two proposed performance shortcuts were rejected: omitting included callables
from bridge verification would change real written-array facts and contributor
input; skipping fresh link observations would weaken replacement detection.
Neither was implemented. Function-pointer formatting now uses Roslyn's output
directly instead of rewriting arbitrary string literal contents.

Repeated managed recipe syntax planning now shares a successful immutable result
only inside one emission-planning call. The key includes the complete projection,
exact carrier spelling and pointer width. Failed conversions are not retained;
parameter identity, channel, ABI and processed-source checks still run at each
original boundary. No shared service, cross-session cache, unchecked plan entry
point or public contract was added. A proposed parallel formatter was removed
after controlled comparisons failed to establish a useful gain.

Frontend identity-prefix reuse is bounded by one NativeModuleBuilder and its
frozen source mapping. Its key retains native cursor identity and the presumed
source mode; only mapped USR and semantic-owner strings are shared. Category,
fallback, location, macro-definition offsets and final hashes stay per-use, and
native cursor handles remain entirely inside the frontend. No fact, layout,
graph ownership or external-input verification is skipped. The locked LLVM
cursor equality and location implementations were also reviewed: the ignored
declaration-group flag does not change these cached prefix inputs.
