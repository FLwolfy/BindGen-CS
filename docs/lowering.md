# Final C++ lowering extension architecture

[简体中文](lowering.cn.md) | [Configuration](configuration-guide.md) | [Architecture](architecture.md)

BindGen-CS uses one lowering pipeline for built-in STL semantics, declarative project rules, compiled plugins, and explicit C ABI shims. The old `ICppTypeAdapter` / `ICppCallableAdapter` contract was deleted; this pre-release line has no compatibility wrapper.

## Choose an extension level

| Level | Use it for | Executes code |
| --- | --- | --- |
| Built-in lowering | Standard strings, containers, smart pointers, paths, and chrono values | BGCS-owned code |
| `TypeLowerings` / `CallableLowerings` | Project types, parameter expansion, naming, and call wrappers expressible as deterministic templates | Controlled templates only |
| Typed lowering plugin | AST matching, target branches, and additional native/managed artifacts | Trusted .NET plugin code |
| `NativeShims` | Compiler-private ABI, complex templates, coroutines, or semantics only project C++ can interpret | Project-owned C/C++ shim |

## Declarative type lowering

```json
{
  "LoweringSafetyPolicy": "AllowUserAsserted",
  "TypeLowerings": [
    {
      "Name": "engine.entity-id",
      "TypePattern": "Engine::EntityId",
      "CAbiType": "uint64_t",
      "Marshalling": "Blittable",
      "Ownership": "Borrowed",
      "ParameterToCppExpression": "Engine::EntityId::FromRaw({value})",
      "ReturnToCExpression": "({value}).Raw()",
      "ManagedProjection": {
        "ManagedType": "EntityId",
        "ManagedToNativeExpression": "{value}.Value",
        "NativeToManagedExpression": "new EntityId({value})"
      },
      "Safety": "UserAsserted"
    }
  ]
}
```

`TypePattern` supports `*` globs. Conversion templates support `{value}`, `{name}`, `{count}`, and `{cppType}`. `AbiParameters` can expand one C++ parameter into multiple C ABI parameters. Managed projections produce deterministic `ConfiguredLoweringProjections.g.cs`; use a plugin-contributed `ManagedSource` artifact for richer friendly APIs.

## Callable lowering

`CallableLowerings` match fully-qualified function patterns, rename or exclude exports, and wrap calls through an expression containing `{invocation}`. They suit error wrappers, context dispatch, and project tracing; they must not hide an invalid ABI type.

## Typed lowering plugins

Plugins register these services through `IBindingPluginHost`:

- `ICppTypeLowering`: type matching, C ABI shape, parameter/return conversion, ownership, allocator, and managed projection;
- `ICppCallableLowering`: callable selection, naming, and invocation lowering;
- `ICppArtifactContributor`: deterministic public-header, native-source, managed-source, or resource artifacts.

Extensions resolve by descending priority and stable name order; duplicate names fail. Implement `ICacheFingerprintProvider` to include mutable extension state in the content-addressed cache key. Unfingerprinted extensions disable cache hits.

## Explicit native shims

```json
{
  "LoweringSafetyPolicy": "AllowUserAsserted",
  "NativeShims": [
    {
      "Name": "engine-coroutine",
      "PublicHeaders": ["shims/coroutine_c.h"],
      "SourceFiles": ["shims/coroutine_c.cpp"],
      "Safety": "UserAsserted"
    }
  ]
}
```

BGCS copies these files into transactional output, includes public headers from `Classes.h`, adds sources to the build manifest, verifies symbols declared with `API(...)`, and can generate matching C# bindings. This is the standard escape hatch for an ABI that cannot be proven directly, not a library-name special case.

## Safety policy and bypass

`LoweringSafetyPolicy` has three levels:

- `VerifiedOnly`: the default; accept only BGCS-verified lowerings;
- `AllowUserAsserted`: accept reviewed project recipes, plugins, and shims;
- `AllowUnsafe`: explicitly bypass the lowering safety gate while emitting the auditable `BGCS-SAFETY-LOWERING-BYPASS` diagnostic.

The C binding side retains `StrictSafetySeverity=Error|SuppressFriendly|Warning`; `StrictSafety=false` explicitly disables inferred ownership/lifetime checks. Project-supplied managed value carriers use `ExternalTypeContracts` with `Reject`, `RequireLayoutMatch`, or `BypassLayoutValidation`, and every accepted by-value carrier is recorded in IR with `BGCS-SAFETY-EXTERNAL-TYPE`. A bypass transfers risk to the project. It cannot make a syntactically unrepresentable ABI valid; provide a lowering or C shim for that case.
