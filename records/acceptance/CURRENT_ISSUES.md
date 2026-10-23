# BGCS 4.0 acceptance ledger

This ledger records source review and local attempts. It is not a release
certificate. Exact-commit CI, installed-product receipts and published package
hashes determine release acceptance. Historical 3.0.1 receipts remain regression
references and never certify 4.0.

## Source review

| Area | Implemented invariant | Required execution evidence |
| --- | --- | --- |
| Composition | Core and Runtime are independent; Engine has no concrete platform dependency. | Independent providers, inline interfaces and installed extensions. |
| Native bridges | C/C++ use typed operations, exact physical signatures and explicit aggregate boundaries. | Native parameter/result/callback consumers and the targeted Browser Mono/AOT regression. |
| Portable sources | Manifest revision 2 separates logical roots from physical input bindings. | Relocated inputs/output, reader mutation and real compilation/invocation. |
| Worker and inputs | Host services select parser resources; final input verification remains independent. | Crash, cancellation, resource corruption and directory-specific path cases. |
| Runtime | Calls, objects, callbacks and operations retain their required module resources. | Concurrent close, quiescence, allocation and cleanup-failure cases. |
| Product boundary | No consumer compiler, linker, deployment or MSBuild integration is shipped. | Architecture, commands, schemas and actual package closure inspection. |

The [issue map](../../eng/acceptance/issue-resolution-map.json) identifies P01–P14
and their owners. The [feature map](../../eng/acceptance/feature-evidence-map.json)
links the 61 retained features to concrete cases. Neither source declaration is
a substitute for executed evidence.

## Retained local attempts — 2026-10-23

All failures below are retained in the local evidence store; they were not
converted into passes by changing budgets, dropping cases or accepting new hashes.

| Attempt | Observation | Concrete correction |
| --- | --- | --- |
| Complete Host discovery/execution | 754 cases executed: 751 passed, 3 failed, none skipped. | Static fixtures now obtain complete target source conventions from the public desktop provider. |
| Configuration baseline | 81/84 before correction; 84/84 after correction. | Assert the friendly value return and exact raw output pointer separately; build and locate the SDK consumer in the same explicit configuration. |
| Public frontend baseline | 210/218 before correction; 218/218 after correction. | Restore eight exact historical input literals. Existing input hashes and public model snapshots stay unchanged. |
| Corpus policy controls | Six policy-identity comparisons rejected checkout line endings. All 20 controls passed after correction. | Hash policy text with its declared LF convention in both producer and collector; retain declaration/source/API comparisons. |
| Affected Host rerun in an explicit Release environment | 61/68 passed; seven consumers compiled successfully but the shared harness looked in Debug. | All nine repository SDK build sites now select Release explicitly and preserve compiler output when the expected assembly is missing. |
| Six-library corpus | Generation, managed warnings-as-errors, 12 native syntax checks and reviewed Windows snapshots passed. The cimguizmo sample was 14.906 seconds against 15 seconds. | Keep the original per-library budgets and require current-source execution; no full upstream application or device invocation is claimed. |

Earlier performance attempts include a narrow 9.98% warm increase, followed by
10.38% and 12.15% failures. The failed measurements remain failures. Ineffective
property-name, short-ASCII and immutable-JSON experiments were removed. Current
code removes duplicate formatting and lookup work, defers unrequested convenience
planning and streams the complete typed model into its emission fingerprint.
These changes require measured acceptance; prose does not close the performance gate.

## Certification policy

- Run the full retained local baseline with explicit Release configuration after
  the batch of corrections. Preserve discovery, selected cases and actual TRX rows.
- Enforce unchanged cold/warm, memory, byte-recovery and relative performance limits.
- Consume isolated candidate archives, including optional extensions and Runtime
  source/single-file variants. Workspace DLLs cannot stand in for those packages.
- Require the three generator checks and installed-product check for the same clean
  commit before immutable tagging and publication.
- Verify public package bytes and clean consumption before declaring release complete.

`implementationReady` records source review only. Release readiness is derived
from executable receipts, never from setting a source status flag to true.
