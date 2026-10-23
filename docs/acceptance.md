# Testing and acceptance

The product stops at generation. Compiling, linking and invoking fixtures below belong to repository consumers, never the SDK, CLI or official extensions.

## Four gates

| Check | Scope |
| --- | --- |
| Generator baseline — Windows x64 | Complete core/Host once, architecture/API/docs, CLI/SDK, six locked corpora and performance. |
| Generator smoke — Linux x64 | Explicit platform differences, real worker/consumption and one fixed Browser Mono Wasm + Wasm AOT aggregate/callback regression. |
| Generator smoke — macOS x64 | SDK/standard library, UTF-32, actual consumption and performance. |
| Installed product baseline | Same-commit candidate archives, clean installation, closure, interface extension, Runtime-only, source Runtime and relocation. |

There are no eight test shards or duplicated full platform matrices. Windows runs all discovered Host cases; smoke hosts run exact registered methods/data rows. Discovery, selection and TRX execution sets must agree. Missing, duplicated, skipped, failed or aborted required cases do not pass.

## Retained coverage

The 61 feature IDs and configuration inventory map to current owners, success/failure/boundary cases and same-source evidence. Coverage includes C/C++, three imports, two Runtime modes, aggregates/callbacks, codecs, allocators, lifetimes, extensions, caches, path safety, workers and publication recovery. Independent Engine provider tests do not reference standard platform implementations.

Equal test counts do not prove behavioral parity. Public generation, semantics, manifests and real consumers replace private-reflection tests; internal node/allocation micro-tests are not presented as public contracts.

## Performance

The 10,000-declaration budgets remain Windows/Linux 60s cold / 10s warm and Intel macOS 120s / 30s. A worker is limited to2GiB and the default session to4GiB. Restore after deleting output must match every file and byte. Final controlled comparison samples five times and records median/variation; repeatable regressions above10% must be resolved or explicitly block acceptance. Do not relax limits or disable input checks.

## Evidence

Source implementation, local compilation, local tests, candidate package consumption, remote CI and public publication are separate states. A dirty workspace or historical3.x receipt cannot certify4.0. Current issues/review live under records/acceptance; execution evidence lives under artifacts. This specification itself asserts no passing gate.
