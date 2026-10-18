# Complete Host test evidence

> Historical plan/checkpoint: the body preserves its original revision and scope, not current usage or mandatory gates. See current [architecture](architecture.md), [acceptance](acceptance.md) and [roadmap](roadmap.md). Removed responsibilities have no runtime compatibility layer.

The Host suite is partitioned into eight class groups on each launch host: Windows x64, Linux GNU x64 and macOS Intel. The other test projects, corpus, performance, package installation and independent browser/NativeAOT gates remain separate requirements. CI executes the Host cases once and reuses their verified union in host acceptance.

The isolated `eng/BGCS.TestDriver` uses the locked SDK's VSTest translation-layer API. It discovers structured `TestCase` objects with xUnit theory pre-enumeration enabled, independently compares them with a metadata-only census of compiled facts/theories, and executes those exact discovered objects. It preserves the case ID, fully qualified method and literal display name, including generic type and argument identities. Inherited methods and derived Fact attributes participate in the census. Unexpanded dynamic theories, omitted compiled methods, ambiguous identities, aborted/partial discovery, skipped cases and failed executions fail the gate.

Class assignment uses the first four bytes of SHA-256 over the UTF-8 class name, interpreted as an unsigned big-endian integer, modulo eight. This keeps every class in one partition without a manually maintained class list. Every shard independently discovers the complete universe before running its assigned cases. The isolated fixture controls exercise actual inherited/generic discovery and execution, literal TRX argument identities, and rejection of collapsed theories and skipped required cases.

The build job prepares one immutable execution archive containing the complete Host, worker, plugin/embedded-host fixtures and driver output trees. The Intel parser supplement and compiler wrapper enter the same archive when required. Its manifest records every file digest. SDK console, translation-layer, object-model, runtime and dotnet-host digests are recorded separately and must match on every consumer job. Restore validates all archive members and hashes before writing, rejects undeclared or duplicate members, confines paths to the exact owned output trees, and refuses different existing bytes. Source revision/content, actual SDK/runtime, runner image and complete execution closure must agree before and after each run.

The completeness job requires all eight successful receipts and their independent raw TRX files. It verifies that every partition has the same complete discovery/census, every selected case passed exactly once, and the executed union equals the discovered universe. It checks raw TRX definitions, results, counters, entries and literal argument names against the structured runner report. The joined TRX preserves the original results and case IDs; it is not a count reconstructed from source files. The joined JSON records each raw report/TRX hash and the complete discovery identities. Missing partitions, skips, duplicate cases, changed arguments, different closures or stale source content fail aggregation.

Host acceptance receives the accepted execution archive and all raw partition evidence. It repeats the independent union/hash checks, binds the union to the current source and compiled closure, then places the canonical Host TRX beside the separately executed test-project receipts. `write-acceptance-report.py` includes the partition receipt hash in the host report. Release requirements name the three exact Host completeness jobs; their dependencies and the all-observed-jobs rule retain every partition as a mandatory gate. Feature verification still requires the reviewed exact methods and literal `requiredCases`; sharding does not change their meaning.

Typical CI commands are:

```sh
dotnet build eng/BGCS.TestDriver/BGCS.TestDriver.csproj -c Release -p:TreatWarningsAsErrors=true -m:1 -nr:false
python scripts/test-host-test-driver.py
python scripts/run-host-test-shards.py prepare --output artifacts/host-test-assets
python scripts/run-host-test-shards.py run --assets artifacts/host-test-assets --shard 0
python scripts/run-host-test-shards.py aggregate --input artifacts/host-test-shards --output artifacts/host-test-complete
```

The aggregate command requires the real receipts from all eight runs. `python scripts/test-host-test-shards.py` exercises omission/tamper rejection with synthetic fixtures; those fixtures never certify product tests. Local dirty diagnostics require the explicit `--allow-dirty-local` option and produce `certifying: false`. A successful clean union remains `releaseReady: false` until every independent release gate succeeds at that revision.

Release publishing uses `verify-accepted` with the authenticated CI run's original execution archive and complete raw union. It hashes every archived component again, verifies the exact eight receipts and literal TRX case universe, and requires the same clean source and selected SDK/runtime. It reads these original bytes without restoring them over newly built release assemblies. This avoids another full Host execution while retaining the actual execution proof from all three mandatory CI hosts; non-Host projects and fresh physical installed-product consumers still execute in the publishing job.