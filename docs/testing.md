# Testing and evidence

[简体中文](testing.cn.md) | [Acceptance](acceptance.md) | [Packages](packages.md)

Required CI has four jobs: Windows full generator tests, Linux generator smoke,
macOS generator smoke, and one full installed-product check. Core feature/configuration
baselines remain mandatory. Optional AOT/Wasm application builds are manual private
checks; they are not additional required CI jobs. This changes execution scheduling,
not supported target facts, source-generation contracts or existing performance limits.

## Generator and core feature checks

```bash
dotnet build BindGen-CS.slnx -c Release
dotnet test BindGen-CS.slnx -c Release --no-build
python scripts/generate-config-docs.py --configuration Release --check
```

Use the pinned .NET 10 SDK/runtime and matched worker/parser resource closure.
Native invocation fixtures also need their explicitly selected compiler, SDK headers
and linker. Those tools belong to the repository fixture, not the generation product.
Corpus tests use locked upstream revisions and independently reviewed source/API
snapshots. Compile/link/call checks may not repair emitted source or silently remove
public declarations. A snapshot candidate is not an automatically accepted baseline.

The full suite covers native facts and closure, ABI/type/layout refusals, typed C++
protocols, all three imports, Runtime delivery, ownership/callback/async cleanup,
source processors, plugins, cache invalidation, worker cancellation/crashes, publication
recovery and path safety. Exact discovery and execution receipts prevent omitted,
duplicate, skipped or failed cases from being reported as a passing full suite.

Repository architecture, API, documentation and supply-chain audits use the non-shipped
`eng/BGCS.RepositoryTools` project. They do not add build or audit commands to the product.
Never write shared assembly outputs while another test or installed consumer uses them.

## Original performance budgets

`eng/acceptance/performance-budgets.json` remains authoritative for 10,000 declarations:

| Host | Cold | Warm |
| --- | ---: | ---: |
| Windows x64 | 60 seconds | 10 seconds |
| Linux x64 | 60 seconds | 10 seconds |
| Intel macOS | 120 seconds | 30 seconds |

The worker/session memory bounds remain 2 GiB / 4 GiB. Every host keeps its budget,
including smoke scheduling. `scripts/test-performance-budget.sh` checks count, complete
artifact ownership and byte-identical restoration; an emission-cache hit alone does
not establish full-pipeline reuse. Stricter explicit overrides must be recorded.

Real-library generation budgets remain cimguizmo 15s, cimgui 30s, SDL3 45s,
miniaudio 60s, bgfx 30s and bimg 15s. Functional runs record timings; dedicated
`BGCS_ENFORCE_GENERATION_BUDGETS=1` runs enforce them on a controlled recorded host.
No timeout increase or missing byte check can turn a failure into a pass.

## Installed products

The required installation job packs the actual four-product set and consumes it in
private clean workspaces. It checks SDK and CLI generation, neutral/advanced plugins,
worker provisioning, Runtime package/source use and the declared archive/resource
closure. It must use the exact recorded package bytes, not a repository project
reference or an older global-cache package. See [packages](packages.md).

## Optional manual AOT/Wasm application checks

```bash
python scripts/test-native-aot-bindings.py
python scripts/test-wasm-bindings.py
python scripts/test-wasm-bindings.py --aot
```

These repository harnesses compile/link generated code and run actual applications.
They may require workload/compiler/browser downloads and substantial disk space;
run them only as an explicitly requested private check. The generator itself does
none of that. Target/runtime capability checks and static adapter source generation
remain normal product functionality.

Keep every assertion of a selected private fixture, including its independent native
oracles and failure-injection controls. Missing prerequisites or failed calls are a
failed/incomplete private check, not success. Their receipts identify actual revision,
host, runtime, toolchain and scope; they do not claim universal platform certification.

## Reporting

Separate source implementation, local dirty-tree proof, clean-revision core acceptance
and release/publication evidence. Keep logs and exact inputs for failures. Old receipts
cannot certify a new source revision; configuring a workflow is not evidence it ran.
