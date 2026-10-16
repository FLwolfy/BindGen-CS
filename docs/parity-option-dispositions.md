# Baseline option dispositions and explicit presentation selection

This audit uses baseline commit `d4bfe3939b6ab2c404080f9da58e4b5f64c861be`. It distinguishes serialized fields from implemented generation behavior. Configuration acceptance rows remain **pending**. Authored tests and source correspondence are not release receipts.

## Fields without an observed baseline emission path

`delegateMappings` (`CFG0070`, with nested `name`, `returnType`, `signature` rows `CFG0181`–`CFG0183`) is initialized, merged and searched by `TryGetDelegateMapping`/`GetDelegateMapping`. A search of all baseline production source finds no caller of those lookups and no analyzer/emitter reading the list. The type-API test only adds a DTO and checks lookup. An arbitrary textual callback-signature replacement is therefore not executed behavior that must be recreated. BGCS 3.0 retains the compiler-established native callback ABI and independently checked managed callback protocols.

`knownConstructors` (`CFG0137`) is initialized, declared and merged, with configuration entry snapshots, but has no baseline analyzer/emitter consumer. New native C++ constructor paths use actual declaration roles and checked bridge operations; explicit C owned factories require matching release protocols. No inferred constructor-routing policy is introduced solely to reproduce an inert dictionary.

`HandleMapping.comment` (`CFG0213`) is stored by its DTO but not read by the baseline handle mapping consumers. The complete `TryGetHandleMapping`/`GetHandleMapping` call-site search identifies `CsCodeGeneratorConfig.Naming.cs`, which reads `friendlyName`, and `NamingPatch.PatchTypedef`, which creates or renames that mapping. The `mapping.comment` used by `BindingModuleAnalyzer` belongs to a `FunctionAliasMapping`, not a `HandleMapping`. The new `api.pointerCarriers[].documentation` provides an explicit inert documentation override for nonowning address views; its tested scope does not imply that the old unused field had been executed.

The new documentation cases are `PointerCarrierTests.AddressDocumentationOverrideRemainsInertEscapedProse` (two namespace/nesting forms; adversarial text remains escaped prose). Their local source/semantic receipts are `cpp-pointer-documentation-two.trx` and the combined `cpp-pointer-carrier-planning-documentation-twenty.trx`. The separate `cpp-pointer-carrier-native-six.trx` covers Windows/CoreCLR pointer behavior across three imports and package/source Runtime; that native run preceded the documentation addition. These local receipts are historical observations, not same-revision release certification.

Reproduce the call-site audit against the exact baseline:

```powershell
git grep -n -e knownConstructors -e GetDelegateMapping -e TryGetDelegateMapping -e delegateMappings d4bfe3939b6ab2c404080f9da58e4b5f64c861be -- src tests/BGCS.Tests/Unit/CsCodeGeneratorConfigTypeApiTests.cs
git grep -n -e TryGetHandleMapping -e GetHandleMapping -e handleMappings d4bfe3939b6ab2c404080f9da58e4b5f64c861be -- src
```

The finding concerns this baseline source. It does not claim anything about unrelated historical versions or external reflection code. The new major version does not preserve the old SDK interfaces or configuration reader.

## Validation metadata is not an executed allocator or scheduler

The baseline `OwnershipAnalyzer` copies `allocatorKind`, `allocatorFunction`, `callbackThreading`, `asyncCompletion` and `completionFunction` into a marshalling plan. `StrictSafetyAnalyzer` uses them to reject unspecified domains, callback lifetimes and missing terminal function identities. These options therefore have executed **validation** effects and must not be described as completely inert.

The old emitter does not call an allocation function selected by `allocatorFunction`, implement a serialized callback scheduler, or execute the selected polling/wait/custom completion function. Its automatic callback helper uses one static `NativeCallback` slot per callable parameter and replaces the previous slot. Metadata describing a native promise does not demonstrate that those runtime mechanisms existed.

BGCS 3.0 separates declared ownership, thread/quiescence evidence and typed runtime operations. Explicit `NativeAllocator` pairs, per-context temporary allocator factories, retained registrations and callback-completion operations have their own checked execution paths. Their actual tests are needed to establish those new guarantees. General new polling, scheduling and retained per-value allocation conveniences remain separate scope; they must not be invented merely because a baseline validation field used the corresponding name.

## Executed selections and their modern equivalents

`generateExtensions` (`CFG0119`), `allowedExtensions` (`CFG0150`) and `ignoredExtensions` (`CFG0143`) operate in `BindingModuleAnalyzer.ResolveManagedPresentation`. They gate automatic extension discovery for free functions whose first argument is an opaque pointer typedef. They do not suppress every friendly conversion. The explicit `knownMemberFunctions` loop runs before that gate and still requests instance members when extension discovery is disabled.

BGCS 3.0 uses `api.memberFunctions` as an explicit whitelist of checked receiver requests. No requests means no selected C member convenience; selecting one does not select a second function. Omitted requests supply the allow/ignore behavior while ordinary context and Raw calls remain available. `kind: value` preserves existing value storage and requires a checked call-only value protocol. Owned and Module-borrowed receivers have independent lifetime requirements.

`generateHandles` (`CFG0121`) primarily selects named nonowning opaque ABI carriers in `TypeAnalyzer.Analyze`, `GetCallbackCarrier` and the typedef/incomplete-record branches of `BindingModuleAnalyzer`. It is not a control that disables owned resource destruction. BGCS 3.0 retains raw pointer carriers and offers explicit `api.pointerCarriers` and `api.pointerFunctions` for optional nonowning address views. Empty selections omit those views. They do not replace native import carriers or prove pointed-to storage lifetime. Omitting address views must retain independently required owned cleanup.

Focused acceptance sources:

- `tests/BGCS.CSharp.Tests/ExplicitPresentationSelectionParityTests.cs`: empty/single/all receiver selections, optional address views across three imports, unchanged raw signatures/storage, final contract checks and independent owned cleanup. These model/source tests do not invoke native code or certify a platform.
- `tests/BGCS.Tests/PresentationSelectionConfigurationTests.cs`: public grouped requests and empty child-array replacement preserving native source and ownership inputs; no parser or SDK child process.

The implementation hooks are `ManagedReceiverProjection.Apply`, `ValueReceiverPlanner.Apply`, `DirectReceiverObjectPlanner.Apply` and `PointerCarrierPlanner.Apply`. No global presentation boolean or post-plan deletion of required ABI storage is added. Test execution results are recorded separately from this rationale.

## Exact baseline file identities

Hashes cover Git blob bytes, independent of working-tree line endings. Recompute with `git show <revision>:<path>` and SHA-256 over the returned bytes. The baseline emitter path was `src/BGCS/Emission/CSharpEmitter.cs`; its working-tree successor path is not used as baseline evidence.

| Baseline path | SHA-256 |
|---|---|
| `src/BGCS/Configuration/CsCodeGeneratorConfig.cs` | `9f025d7f4b9100b20843128a4cade3d70f84988bd1f0c1b6e5044552fcfbc75d` |
| `src/BGCS/Configuration/CsCodeGeneratorConfig.Mappings.cs` | `19e7e69d5270f07c631faadc28ce8ee6dd3d1a8f4de8973d24f3a41d30adf53a` |
| `src/BGCS/Configuration/CsCodeGeneratorConfig.Merge.cs` | `0fa4982747a42cf2f12a60320cfb73e1221514dde7f1f973c68f8927ba5193a8` |
| `src/BGCS/Configuration/CsCodeGeneratorConfig.Types.cs` | `2954ddbee901a0a143ab331329302671db4fece5a8ef4088a9db3a47d1bd4202` |
| `src/BGCS/Configuration/Mapping/DelegateMapping.cs` | `2da93f9ee7237d0cb4dd7629ce1ffab58598b29091de0d229ccba9411cf82f6a` |
| `src/BGCS/Analysis/BindingModuleAnalyzer.cs` | `ae02313fc1ed8bc846864f96c006044b597afa49c5d9fb36f0c8b8e6a79ad020` |
| `src/BGCS/Analysis/TypeAnalyzer.cs` | `290461b06b7d880ff5766c81285ac260f071cd018430faeb071009b0dbfcc83d` |
| `src/BGCS/Emission/CSharpEmitter.cs` | `ef61df744977a08337bb00756cc5917e0edf79f99221aa419b68804f21110f93` |
| `tests/BGCS.Tests/Unit/CsCodeGeneratorConfigTypeApiTests.cs` | `97543e4b764acd775c22d534d7ce3bd8352a88ea29ef7285d701b707d31910ed` |
| `src/BGCS/Configuration/Mapping/HandleMapping.cs` | `09d72285ba70125b7799917a2e42be5fdaa8bfcd00983c7879c343cf25835f1e` |
| `src/BGCS/Configuration/CsCodeGeneratorConfig.Naming.cs` | `90ef44e1045d8d98c96c3296bcdb3c0e66af008d6c2d645c74e7114653a216e1` |
| `src/BGCS/Patching/NamingPatch.cs` | `3f59261f9a5c9269eadcedf15cd434ab886f54465179f9f0eeffd0b7066199ba` |
| `src/BGCS/Analysis/OwnershipAnalyzer.cs` | `0c0c097b83048971322b45cb42c84247f6e451d0ca6e3cb87c5cfcbacbf9a746` |
| `src/BGCS/Analysis/StrictSafetyAnalyzer.cs` | `ff8ff0f3c051498144592f50d1a1dbaa8c8402f66ad1c29a4352de448753bbdd` |
