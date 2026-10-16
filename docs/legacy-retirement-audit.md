# Legacy retirement audit

This audit identifies source ownership and remaining assertion accounting. It does not certify feature parity or release readiness. The frozen configuration inventory, feature inventory and release gates remain authoritative; removing old code does not complete their pending entries.

The detailed, local audit is `artifacts/implementation/legacy-retirement-readiness-audit.json`. It records the exact file lists, source hashes, old test methods and evidence limitations. Its baseline is Git revision `d4bfe3939b6ab2c404080f9da58e4b5f64c861be`; the current working tree is dirty.

## Primary dependency cone

The retired generator occupied 117 production files: the `CsCodeGenerator` facade and configuration, AST analysis/conversion, mutable C# model, old `BindingModule` contracts, old emitter, patches and metadata helpers. Forty-two old test files directly referenced those types; including the inherited configuration fixtures gave 113 affected test files and 311 declared test methods. Their exact hashes are retained in `eng/acceptance/retired-source.json`; these are file sets, not directories to delete recursively.

The prior Roslyn census bound 2,071 reference groups with no unresolved sites. The follow-up audit rechecked source hashes and scanned current sources; its 26 external production token matches are names shared by different types: the neutral declaration graph, new C# overload planner, new lexer options and public `AnalysisResult`. No new production caller of the old cone was found. The follow-up scan is lexical, so a complete build remains a required check after removal.

Three live contract identities were split out of old files: `BindingDiagnostic`, `BindingDiagnosticSeverity` and `BindingImportMode`. Their independent files remain. Runtime source namespace rewriting lives in `BGCS.CSharp/Emission/RuntimeSourceNamespaceRewriter.cs`, used by `RuntimeSourceCatalog`. The live immutability tests moved to Contracts tests, ABI regressions to C# tests, RID assertions to Platforms tests and cache extension regressions to Host tests.

The implemented removal used those exact production and dependent test sets together and removed the Host's direct frontend project reference. The Host retains its Roslyn dependency: managed validation and frozen constant inputs still need it. New grouped fixtures and native consumers remain. Whole `Configuration` or Runtime `Interop` directories were not deleted because they contain live code.

Before retirement the architecture command reported 30 violations: one Host project reference, 28 old Host parser dependencies, and one worker reference used only to choose shared parser assembly identities. The worker now uses a frontend-owned advanced helper. After the source retirement and Host reference removal, the strict source architecture command reports zero violations; compilation and executable acceptance remain separate checks.

## Assertion accounting remains separate

Five grouped configuration port ledgers contain 84 new cases linked to 83 distinct old methods. The inherited configuration fixtures contain 145 declared methods, leaving 62 without an explicit link in those five ledgers. Several have newer focused replacements, including metadata, explicit presentation selection and external function tables. This is an incomplete crosswalk, not evidence of 62 missing product features.

`eng/acceptance/legacy-configuration-assertion-crosswalk.json` now records those 62 remaining methods with their baseline source hashes, exact replacement test methods, policy changes and outstanding consolidation work. Every replacement path and method was checked against current source. These are source correspondences; none is promoted to same-revision release proof by that mapping.

At the original bounded audit, the 33-method IR ledger had 16 focused replacements passed, 15 mappings requiring consolidation, one policy replacement and one partial replacement. The ledger records subsequent exact source and executable assertions; these original counts are not a current completion claim. Two cases require explicit accounting:

- The old layout-validation bypass must become a strict rejection plus a verified pointer or native-shim route. Reintroducing an unchecked bypass would violate the new ABI policy.
- The combined factory/member presentation case includes arbitrary untracked address views. Owned wrappers and module-lifetime borrowed objects have different guarantees. An explicit safe address receiver projection or a documented, compiled consumer partial implementation must account for the remaining assertion; no destructor or lifetime can be inferred from a factory name.

Keep these gaps and the original baseline identities in the durable ledgers when deleting obsolete test implementations. Do not mark entries complete because their old test file disappeared.

The final source cohort adds dedicated `ExactIrRetirementSourceTests` for rejected Clone member reprojection, an explicitly named missing string cleanup, independent missing callback lifetime/threading warnings with strict publication rollback, and the combined single-file constant/record/import assertion. `ExactIrRetirementPlanningTests` separately covers the precise opaque 16-byte/8-aligned by-value rejection and signed three-bit/unsigned one-bit shared storage. These exact assertions must be tracked independently from larger over-aligned aggregate examples. Their discovery identities and passing receipts enter the review proposal only after actual execution; the proposal does not promote either acceptance inventory.

## Secondary cones

The old plugin cone consisted of seven production files: `BindingPluginLoader`, `BindingPluginRegistry` and the five contracts under `Contracts/Extensions`. Those files and two obsolete test implementations are removed, with exact hashes in `eng/acceptance/retired-plugin-source.json`. The fixture now references Contracts alone and implements the new `Contracts/Plugins` API. New loader and Host tests cover contract compatibility, staged duplicate registration, shared contract identity, constructor/fingerprint failures and cache invalidation. Four neutral loader cases and nine Host registry cases subsequently passed locally; their exact receipts remain in the retirement record. Same-revision release execution remains required; no compatibility adapter was added.

The five-file Runtime cone (`NativeLibrary`, `NativeLibraryContext`, `LibraryLoader`, `INativeContext` and the mutable `FunctionTable` under `Runtime/Interop`) and its four obsolete test implementations are also retired. `eng/acceptance/runtime-provider-parity.json` preserves all 19 old-method correspondences and explicit major-version policies. New providers cover exact symbol lookup, owned versus borrowed adopted handles, close races and initialization cleanup. The real custom slot-table consumer demonstrates scrambled native slots, unexported functions, rejected bootstraps, immutable address copying and module retention through an active call. These local replacement results do not certify installed products or other platforms. Live `NativeCallback`, source-location metadata, native-name metadata and other independent Runtime utilities remain.

## Test ownership

| Target project | Meaningful source to move or preserve | Required coordination |
|---|---|---|
| Contracts | Neutral `BindingImmutabilityTests` and actual contract/version assertions | Keep serialization implementation tests with their owner. |
| Platforms | Pure target triple, RID and capability resolution tests | Split mixed parse/build fixtures rather than moving them wholesale. |
| Host | Existing new configuration, plugin, cache, workspace and Host tests | Remove old API tests first; preserve fixture discovery. |
| Architecture | Existing reflection and dependency boundary tests | Keep CLI architecture command coverage; do not duplicate or weaken its validator. |
| NativeInterop | Existing real `Native*` consumers and their harness | Update exact TRX project names, test proof identities, project references and fixture-root lookup. |
| Package | Actual archive, dependency graph and clean installed-product gates | Use real accepted package inputs; an empty xUnit wrapper adds no evidence. |
| Performance | Existing 10,000-declaration process and RSS budget harness | Preserve controlled-runner measurements and phase receipts. |

Pure analysis tests currently in Worker tests can move to Analysis; protocol, crash and worker plugin tests stay with Worker. Any move must preserve the discovered-test union and exact release proof mappings. Production source must not use `Compile Link` or friend access to conceal a boundary problem.

## Validation after removal

Compile the full solution, run strict architecture and public API checks, then execute the remaining substantive tests. Reconcile assertion crosswalks and moved proof identities without promoting pending release evidence. Fresh same-revision desktop, browser, NativeAOT, package installation, corpus, performance and supply-chain gates still determine release readiness.
