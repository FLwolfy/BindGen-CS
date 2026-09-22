# Configuration Guide

[Wiki](README.md) | [中文](configuration-guide.cn.md) | [Configuration entries with dedicated tests](config.md)

The public JSON model remains flat for source compatibility, while the internal pipeline separates input, target, analysis, marshalling, emission, and output responsibilities. Layer large configurations through BaseConfig and presets instead of copying one monolithic document.

Use configuration sources in this order of authority:

1. `bindgen-cs schema` from the installed version for the complete property set;
2. this guide for workflow and safety rules;
3. `docs/config.md` for behavioral examples backed by dedicated regression tests.

`docs/config.md` is not a complete property inventory and does not replace the schema.

Generate an editor/CI schema with:

```bash
bindgen-cs schema bindgen.schema.json
```

The schema is derived from the installed `CsCodeGeneratorConfig` version, including enum names and discoverable defaults. It is useful for property discovery; detailed semantics for object-valued mappings remain in this guide and the tests.

## Make four decisions first

| Decision | Common choice | When to change it |
| --- | --- | --- |
| Language boundary | C header → C# | Use a C bridge for C++ classes/templates |
| Target | `host-c` | Select an explicit target/triple/sysroot for a non-host ABI |
| Import | `DllImport` | Use `LibraryImport` for source generation or `FunctionTable` for runtime loading |
| Runtime | Reference `BGCS.Runtime` | Enable `GenerateRuntimeSource` for standalone source distribution |

## Minimal configuration

```json
{
  "Namespace": "MyCompany.Native.Library",
  "ApiName": "LibraryApi",
  "LibName": "library",
  "Preset": "host-c,c-library",
  "EntryFiles": ["include/library.h"],
  "IncludeFolders": ["include"],
  "ImportType": "DllImport",
  "OutputPath": "Generated"
}
```

This configuration follows the host ABI. Reproducible release configurations should use an explicit target preset or set platform/architecture/ABI directly; the configured target must match the final native binary.

## Input and parser settings

| Property | Purpose | Guidance |
| --- | --- | --- |
| `EntryFiles` | Root headers | Prefer one stable umbrella header when available |
| `AllowedHeaders` | Explicit emission whitelist | Empty plus transitive mode is easier for umbrella headers |
| `IncludeTransitivelyReferencedHeaders` | Include user headers below entry/include roots | Recommended for SDL-style APIs |
| `IncludeFolders` | User include roots | Declarations can be emitted in transitive mode |
| `SystemIncludeFolders` | Compiler/system include roots | Normally not emitted |
| `Defines` | Preprocessor defines | Match the native library build exactly |
| `AdditionalArguments` | Raw Clang arguments | Use only when a typed setting is unavailable |
| `ParserKind` | `C`, `Cpp`, or `ObjC` | Match the actual header language |
| `ParseMacros` | Build macro AST | Disable for macro-heavy libraries if constants are unnecessary |
| `ParseComments` | Build documentation AST | Disable for speed only when docs are unnecessary |
| `ParseSystemIncludes` | Include system declarations | Keep false unless deliberately binding them |
| `AutoSquashTypedef` | Collapse typedef chains | Disable when public aliases must be preserved |

## Target settings

`TargetPlatform`, `TargetArchitecture`, and `TargetAbi` form a validated target. `Host` resolves to the running platform/architecture; explicit targets cover Windows, Linux, macOS, Android, iOS, and FreeBSD with their valid x86/x64/Arm/Arm64 combinations. `TargetTriple`, `TargetSysRoot`, and `CompilerPath` provide controlled overrides. Defines and native binaries must match the resolved target. Host parsing discovers compiler system includes, and macOS additionally discovers the active SDK.

Model support is not the same as completed host acceptance. See the [target evidence matrix](capabilities.md#target-evidence) and the current generated acceptance report.

## Import modes

- `DllImport`: broad compatibility and simple diagnostics.
- `LibraryImport`: source-generated imports; signatures must satisfy source-generator restrictions.
- `FunctionTable`: explicit native context and symbol resolution, matching the current Inno.Native style.

## Output and Runtime

- `OutputPath` is resolved relative to the config file by `GenerateConfigured`.
- `SingleFileOutputName` must be a file name ending in `.cs`; paths are rejected.
- `GenerateRuntimeSource=false` expects a `BGCS.Runtime` reference.
- `GenerateRuntimeSource=true` emits guarded standalone Runtime source.
- Output is transactional; failed parsing/generation does not delete previous successful output.

## Mappings and policies

Use mappings only for facts that cannot be inferred safely:

- native/managed naming;
- opaque or unexposed types;
- string encoding and ownership;
- pointer/count relationships that names cannot identify;
- constructors and member-style functions;
- explicit template instantiations and C++ adapters.

A mapping must not hide ABI uncertainty. If a non-trivial C++ type crosses a boundary, generate a C bridge instead.

## Strict safety diagnostics

`StrictSafety` defaults to `true`. `StrictSafetySeverity` selects `Warning` (diagnose while preserving compatibility), `SuppressFriendly` (keep raw ABI and remove high-risk string/Span/array/delegate overloads), or `Error` (make validate/generate/build fail before commit). Diagnostics use `BGCS-SAFETY-*` codes and include the exact minimum `MarshallingMappings` path. Set `StrictSafety=false` only when an external audit owns those semantics.

## Ownership and buffer marshalling

Use `MarshallingMappings` when pointer syntax cannot express ownership or buffer relationships:

```json
{
  "MarshallingMappings": {
    "library_create_name": {
      "Return": {
        "Strategy": "String",
        "Ownership": "Owned",
        "Encoding": "Utf8",
        "CleanupFunction": "library_free_name",
        "RequiresCleanup": true,
        "NullTerminated": true
      }
    },
    "library_get_items": {
      "Parameters": {
        "output": {
          "Strategy": "Span",
          "Ownership": "CallerAllocated",
          "LengthParameter": "actual_count",
          "CapacityParameter": "capacity",
          "WrittenCountParameter": "actual_count"
        }
      }
    }
  }
}
```

Explicit mappings override conservative inference and are preserved by `OverloadPlanner`. They are available to all shared-IR emitters through `MarshallingPlan`.

## Typed C variadic functions

Unconfigured `...` functions are skipped with a diagnostic because silently dropping variadic arguments is ABI-unsafe. Define promoted fixed variants for Windows DllImport:

```json
{
  "VariadicFunctionVariants": {
    "native_log": [
      {
        "Suffix": "IntString",
        "ParameterTypes": ["int", "byte*"],
        "ParameterNames": ["value", "text"]
      }
    ]
  }
}
```

C default argument promotion must already be reflected in the configured types: use `double` rather than `float`, and `int` rather than narrow integer types. Each variant uses the original native EntryPoint.

## Base configuration

```json
{
  "BaseConfig": {
    "Url": "file://shared.windows-x64.json",
    "IgnoredProperties": ["EntryFiles", "OutputPath"]
  }
}
```

Relative base files resolve from the referring config. Circular references fail with a clear error. Loading an existing config never rewrites it.

## Presets

Presets are composable and generic: choose one target preset (`host-c`, `host-cpp`, `windows-c`, `windows-cpp`, `linux-c`, `linux-cpp`, `macos-c`, or `macos-cpp`) and optionally add API/output policies such as `c-library`, `function-table`, and `opaque-callbacks`. Library-specific facts stay in the consuming project's configuration rather than in BindGen-CS core.

```json
{
  "Preset": "host-c,c-library,opaque-callbacks",
  "EntryFiles": ["vendor/SDL/include/SDL3/SDL.h"],
  "IncludeFolders": ["vendor/SDL/include"]
}
```

Explicit project settings override preset defaults, independent of preset order.

## Workspaces

A workspace stores multiple configuration paths for repository-level automation:

```bash
bindgen-cs workspace validate native/bindings/workspace.json
bindgen-cs workspace generate native/bindings/workspace.json
bindgen-cs workspace diff native/bindings/workspace.json
```

Put `workspace diff` in CI to validate all checked-in bindings without overwriting final output.
