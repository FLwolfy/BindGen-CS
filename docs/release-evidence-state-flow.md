# Implementation review and release evidence

[Acceptance](acceptance.md) | [Release verification](release-verification.md) | [Publishing](publish.md)

Keep these states separate:

| State | Evidence |
| --- | --- |
| Source implemented | Current models, implementation and validation path exist. |
| Source reviewed | Dependencies, API, scoped correspondence and safety rules were reviewed. |
| Local execution | Named checks ran against recorded bytes/host; dirty results remain local. |
| Core acceptance | Current mandatory baselines and selected CI/install checks passed for one source identity. |
| Published release | Applicable release/package/provenance and public-availability checks actually completed. |

The required CI roles are Windows full generator checks, Linux/macOS generator smoke
and one full product-install job. Original per-host performance budgets remain required.
AOT/Wasm application builds/runs are optional manual private checks, not an extra state
required for core acceptance. Their target facts and source generation remain in scope.

Inventories and scope-removal records preserve exact requirements and reviewed
replacements. `implementationReady`, an old receipt or a configured workflow does not
certify execution. A removed build/Objective-C/integration responsibility cannot be
recorded as passing. Required tests may not disappear through silent selectors.

A release request must use the applicable current workflow and exact source/package
identity. Missing, stale or contradictory required evidence blocks that request.
Package creation, upload, public indexing and installed availability are distinct
steps. Existing published tags are immutable; documentation does not authorize tag
movement or public writes. Consult [publishing](publish.md) for the actual procedure.
