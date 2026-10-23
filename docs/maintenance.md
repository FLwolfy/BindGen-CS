# Maintenance contract

BGCS aims for a stable, tested core, not a promise that only .NET upgrades can require changes.

1. Try an existing interface first: target inputs, ABI classification, runtime capabilities, native lowering, semantic configuration or managed projection.
2. If it cannot express a requirement, record the exact missing representation and the smallest contract change.
3. Fix reproduced generator or Runtime bugs in their responsible module. Do not distribute a library-specific workaround across emitters.
4. Keep source identity, cleanup, ABI checks and complete input tracking intact while optimizing.
5. Retest the affected supported combinations after parser, SDK, runtime or ABI changes.

New SDK paths, macros, existing-model ABI rules, library protocols and symbol providers normally belong in interface implementations. A new language mechanism, physical call mechanism or previously unmodeled lifetime protocol can need a core change.

Support claims distinguish parsed, generated, compiled by a repository consumer and actually invoked. An installed resource or a declared capability is not runtime certification. Current evidence and unresolved issues belong to `records/acceptance`, not marketing claims.
