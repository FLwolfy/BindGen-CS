# Release verification

A local passing test is diagnostic evidence. Release certification requires the clean release revision, its complete discovered tests, installed product packages, and the corresponding CI receipts.

`eng/acceptance/required-release-gates.json` is the release job and receipt policy. The publishing workflow checks every required CI job and rejects any observed failed, cancelled or skipped job. Installed-product evidence must come from the package workflow associated with that exact successful main push.

Each completed feature in `feature-inventory.json` also needs `implementation`, `requiredHosts`, and `releaseEvidence`. A deliberately narrower platform scope requires `scopeJustification`. A test mapping identifies an actual discovered suite and matching executed cases:

```json
{
  "featureId": "example.feature",
  "status": "complete",
  "implementation": ["src/BGCS.Analysis/Example.cs"],
  "requiredHosts": ["windows-x64", "linux-x64", "macos-x64"],
  "releaseEvidence": [
    { "host": "windows-x64", "kind": "test", "project": "BGCS.Tests", "match": "ExampleTests\\.RoundTrip" },
    { "host": "linux-x64", "kind": "test", "project": "BGCS.Tests", "match": "ExampleTests\\.RoundTrip" },
    { "host": "macos-x64", "kind": "test", "project": "BGCS.Tests", "match": "ExampleTests\\.RoundTrip" }
  ]
}
```

Category mappings use `kind: "category"` and the exact category name in the structured full acceptance report. An unmatched expression, skipped case, failed category, dirty checkout, prebuilt-output run, stale revision or changed source digest fails certification. `scripts/verify-feature-release-evidence.py` derives the per-feature proof from same-revision CI reports; a committed local receipt is insufficient.

Configuration parity is checked independently. `configuration-baseline.json` freezes the original 243 `configurationId`/source/member/type identities and their order. The current inventory may append new IDs, but cannot remove, reorder or retarget the frozen prefix. Every row needs the same complete status, actual implementation files and host proofs, plus an `acceptanceRationale` explaining the concrete assertions. Configuration proofs use an exact anchored, escaped fully qualified test method, for example `^BGCS\.Configuration\.Tests\.ExampleTests\.RoundTrip$`. They cannot use a whole passing suite or generic category. Relevant rows may share a test that actually asserts each behavior. `implementationMapping` remains a source correspondence record and never supplies release acceptance automatically.

The isolated SDK and workload manifests are locked in `eng/toolchains`. CI installs the exact SDK, verifies its runtime, installs workloads without updating manifests, and verifies manifest hashes and required packs. Native compiler and SDK facts are captured from actual discovery/build execution; a mutable hosted runner label does not pin their versions.

Packaging retains independent public SDK, worker and TaskHost closures. Dependency notices include their union. NuGet `supply-chain` requires the complete clean, stable `package-verification.json` inventory and verifies its exact archive hashes and revision before producing output. It reads the delivered nupkg/snupkg files, verifies the copied license/nuspec evidence and actual execution dependency graphs, hashes archive entries, and emits transitive SPDX relationships and SLSA source materials. Intel macOS parser provenance additionally describes actual relocated dylib owners, link dependencies, source identities and delivered license texts. Unknown licenses are never inferred from filenames. GitHub OIDC attestations are a separate release action.

After pushing all declared products and technical RID packages, publishing waits for every public NuGet version index, compares every uncompressed package entry with the accepted payload (allowing NuGet repository signatures), and runs clean public-feed SDK, tool, Contracts and Runtime consumers. GitHub release publication follows that verification. These public-feed checks supplement the earlier complete installed native/AOT/browser matrix; they do not replace it.

The workflow uses the supported six-hour hosted-job limit for full validation. A complete same-revision timing receipt remains necessary. If deterministic class sharding becomes necessary, its discovery list, disjoint partition proof and merged executed-case completeness must be validated before it can replace a full-suite gate.
