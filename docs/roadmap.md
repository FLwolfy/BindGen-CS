# Generator-only convergence roadmap

[简体中文](roadmap.cn.md) | [Architecture](architecture.md) | [Acceptance](acceptance.md)

The current plan has eleven product projects, four products and one source-generation
pipeline. No legacy facade, Objective-C product, native-build execution, consumer layout
probe, remote configuration or BGCS MSBuild integration remains part of the product.
Historical plans record their original scope and are not compatibility obligations.

1. Finish the core feature/configuration baseline correspondence and explicit scope removals.
2. Review module ownership, coupling, neutral ports and lifetime/cache invariants.
3. Run current local generator checks and unchanged per-host performance budgets.
4. Run Windows full generator CI, Linux/macOS smoke and one full product-install job.
5. Review exact receipts and package identities before the applicable release procedure.

AOT/Wasm application compilation/invocation is optional manual private work. It does
not authorize weakening target facts, source generation, ABI checks or selected fixture
assertions. Unknown ownership and unsupported ABI still require refusal or a shim.

CLI and embedded SDK use `BindingHost`; consumers compile/link/package/deploy outputs.
Plugins map inputs/targets and extend typed lowerings or presentation; they do not
magically provide a proprietary SDK, new runtime or unknown ABI classifier.

Source implementation, source review, local execution, core acceptance and published
release are separate evidence states. No checked-in status or past successful revision
substitutes for current executed baselines. See [assessment](assessment.md),
[testing](testing.md) and [release verification](release-verification.md).
