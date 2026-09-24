# Diagnostics Guide

[简体中文](diagnostics.cn.md) | [Documentation index](README.md) | [Configuration guide](configuration-guide.md)

BindGen-CS safety diagnostics mean that the native declaration does not contain enough semantics for a friendly managed API. Supply the minimum explicit configuration instead of disabling checks or forcing a non-trivial type into a blittable struct.

## Recommended investigation order

```bash
bindgen-cs doctor
bindgen-cs validate bindgen.json
bindgen-cs inspect bindgen.json
bindgen-cs inspect bindgen.json --json
bindgen-cs explain BGCS-SAFETY-OWNERSHIP
bindgen-cs explain --json
```

1. `doctor` confirms compiler, system includes, target triple, and SDK discovery.
2. `validate` exposes parser, ABI, and safety issues before output is written.
3. `inspect` reports the resolved target, type/function counts, and IR diagnostics.
4. Use `--json` to inspect the Binding IR when lowering needs investigation.
5. Use `explain` to retrieve the versioned diagnostic catalog; omit the code to list every descriptor.

## StrictSafety modes

| `StrictSafetySeverity` | Behavior |
| --- | --- |
| `Warning` | Keep the raw ABI and current friendly API while reporting risk |
| `SuppressFriendly` (default) | Keep the raw ABI but remove inferred friendly overloads for the affected function |
| `Error` | Promote the risk to an error and fail before committing final output |

Use `StrictSafety=false` only when an external process completely audits these semantics. It does not make unknown ownership correct.

## Diagnostic codes

| Code | Cause | Recommended fix |
| --- | --- | --- |
| `BGCS-SAFETY-OWNERSHIP` | A pointer return has no ownership contract | Set `MarshallingMappings.<function>.Return.Ownership`, plus `CleanupFunction` and `RequiresCleanup` when needed |
| `BGCS-SAFETY-CALLBACK` | Callback retention/unregister lifetime is not declared | Add a parameter marshalling mapping and make the consumer retain or unregister the callback correctly |
| `BGCS-SAFETY-LENGTH` | A buffer pointer has no proven length/capacity relationship | Set `LengthParameter`, `CapacityParameter`, and optionally `WrittenCountParameter` |
| `BGCS-SAFETY-ALLOCATOR` | An output string has no cleanup allocator | Set `CleanupFunction`, ownership, encoding, and cleanup requirement |
| `BGCSCS001` | The IR-native C# emitter cannot preserve a declaration without semantic loss | Implement a general, tested IR lowering or explicitly exclude the declaration; emission fails before writing output |
| `BGCSCPP-INSTANTIATION` | A primary template was found without a requested concrete instance | Add the required full specialization to `TemplateInstantiations` or `FunctionTemplateInstantiations` |
| `BGCSCPP001` | A C++ declaration has no accepted lowering | Configure a built-in type list, request a concrete template instance, add a declarative lowering/typed plugin/explicit C shim, or choose an auditable safety policy |
| `BGCS-SAFETY-LOWERING-BYPASS` | An `Unsafe` lowering was explicitly allowed | Keep project-owned ABI, native invocation, allocator, and lifetime tests; promote the lowering to `UserAsserted` or `Verified` when evidence exists |
| `BGCS-SAFETY-EXTERNAL-TYPE` | A project-supplied managed value carrier crosses the ABI | Declare matching target size/alignment with `RequireLayoutMatch`, use pointer-only semantics, or explicitly bypass and retain native invocation tests |

## Minimal mapping example

```json
{
  "StrictSafety": true,
  "StrictSafetySeverity": "Error",
  "MarshallingMappings": {
    "library_get_items": {
      "Parameters": {
        "items": {
          "Strategy": "Span",
          "Ownership": "CallerAllocated",
          "CapacityParameter": "capacity",
          "WrittenCountParameter": "count"
        }
      }
    },
    "library_create_name": {
      "Return": {
        "Strategy": "String",
        "Ownership": "Owned",
        "Encoding": "Utf8",
        "CleanupFunction": "library_free_name",
        "RequiresCleanup": true,
        "NullTerminated": true
      }
    }
  }
}
```

## Parser and target problems

- Header not found: paths resolve from the configuration directory; check `EntryFiles`, `IncludeFolders`, and filename casing.
- An umbrella header emits no declarations: enable `IncludeTransitivelyReferencedHeaders` and keep user headers under the entry directory or `IncludeFolders`.
- Platform types do not match: do not copy another platform's `Defines`; inspect `TargetPlatform`, `TargetArchitecture`, `TargetAbi`, `TargetTriple`, and `TargetSysRoot`.
- macOS standard library/SDK is missing: run `xcrun --show-sdk-path`, then use `doctor`; set `SDKROOT` or `CompilerPath` only when discovery needs an override.
- A C++ symbol cannot be P/Invoked: methods/functions without C linkage belong behind `bridge`; a mangled name is not a stable ABI.

## What not to do

- Do not map an unknown C++ class to `nint` and pretend its lifetime is handled.
- Do not hand-edit generated `[DllImport]`, layout, or cleanup code.
- Do not use `StrictSafety=false` as a substitute for project ownership documentation.
- Do not let the binding target configuration diverge from the ABI of the native binary.
