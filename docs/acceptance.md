# Core feature acceptance

[简体中文](acceptance.cn.md) | [Testing](testing.md) | [Assessment](assessment.md)

Acceptance is pass/fail against the current core feature and configuration baselines.
A removed product responsibility is recorded as an explicit scope removal, never as
a passing feature. Source implementation, local execution and release certification
are separate states. This specification does not assert that current gates passed.

## Required CI scope

The required plan has three generator jobs and one full product-install job:

| Job role | Required scope |
| --- | --- |
| Windows generator | Complete current generator regression/baseline checks |
| Linux generator | Current generator smoke and its recorded host checks |
| macOS generator | Current generator smoke and its recorded host checks |
| Installed products | Actual full SDK/tool/Contracts/Runtime package consumption |

All original per-host performance budgets remain in force. Exact selected suites and
job/receipt identities belong to the workflows and
[required-release-gates.json](../eng/acceptance/required-release-gates.json). Do not
silently convert a failed baseline, skipped selected test or missing input into success.
AOT/Wasm application build/run checks are optional manual private work; target facts,
capability refusals and source generation for them remain product functionality.
Visual Studio TaskHost and BGCS MSBuild integration are outside the product scope.

## Core proof

- Public C declarations/types/constants/macros, dependency closure and preserved facts.
- Ordinary C ABI, layout/calling classification and explicit refusal of unsupported forms.
- Typed C++ objects/templates/family protocols, casts, callbacks and exception boundaries.
- Three imports, Raw/friendly surfaces, Runtime package/source and lifecycle behavior.
- Neutral plugin activation, input/fingerprint/cache behavior and worker isolation.
- Deterministic source/API snapshots, final emitted-contract checks and safe publication.
- Independent compiler/link/call fixtures where the baseline requires actual invocation.
- Clean installed products, schema/resource closure and dependency/license integrity.

The generator emits source and assertions. Repository tests may compile/link/call them;
that does not reintroduce a product build pipeline. Size equality, a target name or a
user assertion cannot become verified ABI proof. Snapshot changes require source/API
review; generated declarations must not be removed to make a fixture compile.

## Performance remains unchanged

The 10,000-declaration test verifies count, every artifact byte and full restoration
after deleting owned output. Budgets are Windows/Linux x64 60s cold / 10s warm and
Intel macOS 120s cold / 30s warm; worker/session memory limits are 2 GiB / 4 GiB.
[performance-budgets.json](../eng/acceptance/performance-budgets.json) is authoritative.
An emission-cache hit is not full-pipeline proof. Stricter explicit limits are recorded.

Real-library dedicated limits remain cimguizmo 15s, cimgui 30s, SDL3 45s, miniaudio 60s,
bgfx 30s and bimg 15s. Functional corpus runs record timings; controlled
`BGCS_ENFORCE_GENERATION_BUDGETS=1` runs enforce those library budgets. OOM, incomplete
output, unsafe source edits and missing required checks remain failures.

## Evidence and release

Inventories map exact current implementation and executable assertions. Stable IDs,
source/API snapshots and scope-removal records prevent historical tests from certifying
a replacement automatically. A discovered test is not an executed test. Receipts retain
revision, source identity, dirty state, host, selected case set and actual tool inputs.
Dirty/local subsets cannot certify an immutable release commit or another platform.

Source/API/architecture checks enforce eleven project boundaries and Contracts/Runtime
independence. No parser handles leak through neutral ports, no production friends or
shared production source bypass the boundaries, and unknown lifetimes remain unknown.

Release additionally follows its applicable package, provenance and public-availability
procedure. AOT/Wasm private receipts remain labeled optional with actual scope. Neither
a configured workflow nor this document grants `releaseReady`. See
[release verification](release-verification.md), [packages](packages.md) and
[publishing](publish.md).
