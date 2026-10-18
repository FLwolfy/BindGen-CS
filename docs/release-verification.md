# Release verification

[简体中文](release-verification.cn.md) | [Acceptance](acceptance.md) | [Publishing](publish.md)

Core feature/configuration baselines are mandatory. Required CI consists of Windows
full generator checks, Linux/macOS generator smoke and one full installed-product job.
All original per-host performance budgets remain unchanged. AOT/Wasm application
publication/invocation is optional manual private work, not mandatory release CI.
Target facts and source-generation behavior remain subject to core baselines.

Exact workflows, selected cases and receipt policy are represented by
`eng/acceptance/required-release-gates.json` and the current inventories. These files
must agree with actual executed cases; source presence or `implementationReady` is
not a substitute for a matching executable receipt. Explicit removed responsibilities
belong to `scope-removals.json`, not to fabricated passing evidence.

Receipts identify source revision/digest, clean or dirty state, host, package/tool bytes
and actual assertions. Missing, failed or skipped required cases cannot certify the
selected suite. Historical reports and a successful dirty local subset cannot certify
another immutable revision. Reviewed source/API snapshot changes retain their exact
scope and do not weaken ABI or ownership assertions.

The product generator does not compile consumer/native projects, execute layout probes
or inspect native object files. Repository fixtures and installation checks may do
external compilation/invocation as required evidence. Optional private application
checks retain all of their selected assertions and report their actual prerequisites
and scope; an unavailable prerequisite is not a passing check.

Packaging verifies the declared physical archive set, dependency/resource closure,
licenses and source/package identities. SDK worker payload uses standard NuGet content
and verified local first-use provisioning, without BGCS build targets or TaskHost.
Publishing follows accepted clean-revision checks and verifies actual public package
availability. This document does not state that a release is complete or published.
