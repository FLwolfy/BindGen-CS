# Configuration Guide

[Wiki](README.md) | [中文](configuration-guide.cn.md) | [Configuration entries with dedicated tests](config.md)

The current JSON model is flat; the internal pipeline separates input, target, analysis, marshalling, emission, and output responsibilities. Layer large configurations through BaseConfig and presets instead of copying one monolithic document. No earlier pre-release configuration format is loaded.

Use configuration sources in this order of authority:

1. `bindgen-cs schema` from the installed version for the complete property set;
2. this guide for workflow and safety rules;
3. `docs/config.md` for behavioral examples backed by dedicated regression tests.

`docs/config.md` is not a complete property inventory and does not replace the schema.

Generate an editor/CI schema with:

```bash
bindgen-cs schema bindgen.schema.json
bindgen-cs schema bridge.schema.json --kind cpp
```

Schemas are derived from the installed C or C++ configuration type, include nested public object shapes, enum names, and core semantic descriptions, and reject unknown root properties by default. Use `--allow-unknown-properties` only when a controlled outer tool owns additional metadata; it does not enable old schema migration. Detailed marshalling semantics remain in this guide and the tests.

## Make four decisions first

| Decision | Common choice | When to change it |
| --- | --- | --- |
| Language boundary | C header → C# | Use a C bridge for C++ classes/templates |
| Target | `host-c` | Select an explicit target/triple/sysroot for a non-host ABI |
| Import | `DllImport` | Use `LibraryImport` for source generation or `FunctionTable` for runtime loading |
| Runtime | Reference `BGCS.Runtime` | Enable `generateRuntimeSource` for standalone source distribution |

## Minimal configuration

```json
{
  "namespace": "MyCompany.Native.Library",
  "apiName": "LibraryApi",
  "libName": "library",
  "preset": "host-c,c-library",
  "entryFiles": [
    "include/library.h"
  ],
  "includeFolders": [
    "include"
  ],
  "importType": "DllImport",
  "outputPath": "Generated"
}
```

This configuration follows the host ABI. Reproducible release configurations should use an explicit target preset or set platform/architecture/ABI directly; the configured target must match the final native binary.

`ConfigVersion` identifies the JSON contract. `init` writes the current version; this pre-release build accepts only that version and provides no old-schema migration.

## Input and parser settings

| Property | Purpose | Guidance |
| --- | --- | --- |
| `entryFiles` | Root headers | Prefer one stable umbrella header when available |
| `allowedHeaders` | Explicit emission whitelist | Empty plus transitive mode is easier for umbrella headers |
| `includeTransitivelyReferencedHeaders` | Include user headers below entry/include roots | Recommended for SDL-style APIs |
| `includeFolders` | User include roots | Declarations can be emitted in transitive mode |
| `systemIncludeFolders` | Compiler/system include roots | Normally not emitted |
| `defines` | Preprocessor defines | Match the native library build exactly |
| `additionalArguments` | Raw Clang arguments | Use only when a typed setting is unavailable |
| `parserKind` | `C`, `Cpp`, or `ObjC` | Match the actual header language |
| `parseMacros` | Build macro AST | Disable for macro-heavy libraries if constants are unnecessary |
| `parseComments` | Build documentation AST | Disable for speed only when docs are unnecessary |
| `parseSystemIncludes` | Include system declarations | Keep false unless deliberately binding them |
| `autoSquashTypedef` | Collapse typedef chains | Disable when public aliases must be preserved |

## Target settings

`TargetPlatform`, `TargetArchitecture`, and `TargetAbi` form a validated target. `Host` resolves to the running platform/architecture; explicit targets cover Windows, Linux, macOS, Android, iOS, FreeBSD, and Emscripten wasm32 with their valid architecture combinations. `targetTriple`, `targetSysRoot`, and `compilerPath` provide controlled overrides. C# and C++ configuration paths expand environment variables before deciding whether a compiler value is a command name or a configuration-relative path; both absolute and relative path values are supported. Defines and native binaries must match the resolved target. Host parsing discovers compiler system includes, and macOS additionally discovers the active SDK. For Emscripten, provide the matching SDK sysroot explicitly.

Model support is not the same as completed host acceptance. See the [target evidence matrix](capabilities.md#target-evidence) and the current generated acceptance report.

## Import modes

- `DllImport`: broad compatibility and simple diagnostics.
- `LibraryImport`: source-generated imports; signatures must satisfy source-generator restrictions.
- `FunctionTable`: caller-owned native context and explicit symbol resolution; the generated API has no dependency on a consumer's loader or engine.

Opaque handle wrappers retain their managed API while native imports carry pointer-sized `nint` values on every target. Callback signatures use the same recursive type analysis as ordinary parameters, including canonical record aliases and native pointer carriers for incomplete records. These rules apply to all ABIs; they do not depend on a browser target or a library-specific type name.

## C# emission backend

`CSharpEmissionBackend` is a future extension point and currently accepts only `IntermediateRepresentation`. Canonical `BindingModule` emits raw ABI plus public string/span/ref/out friendly overloads, covering constants, enums, aliases, opaque handles, delegates, anonymous/nested records, fixed arrays, bitfields, and all import modes. Unsupported semantics fail with `BGCSCS001` before transaction commit. There is no fallback emitter, and a failed run preserves last-good output.

## Output and Runtime

- `outputPath` is resolved relative to the config file by `GenerateConfigured`.
- `mergeGeneratedFilesToSingleFile=true` combines generated bindings into one C# file per target. `oneFilePerType` is independent; set it to `false` if you also want to avoid per-type files before composition. The `c-library` preset already sets both for one `Bindings.cs`.
- `singleFileOutputName` must be a file name ending in `.cs`; paths are rejected.
- `generateRuntimeSource=false` expects a `BGCS.Runtime` reference.
- `generateRuntimeSource=true` emits guarded standalone `Runtime.cs` separately, even with single-file bindings.
- Output is transactional; failed parsing/generation does not delete previous successful output.

## Incremental cache and plugins

`enableIncrementalCache` defaults to `true`; `cacheDirectory` defaults to `.bindgen-cache` relative to the configuration file. A key contains the installed generator identity, complete serialized configuration, parser arguments, resolved compiler identity/version, plugin/lowering fingerprints, and exact contents of discovered C/C++ inputs. Restore and publication are transactional. Changing a header, target, toolchain, define, include, mapping, plugin binary, shim, or generator binary creates a different key. Programmatic generators with unfingerprinted custom state bypass cache hits rather than risk stale output.

`pluginAssemblies` lists explicit assembly paths relative to the configuration file. Each assembly must expose a public parameterless `IBindingPlugin` and return `BindingPluginContract.C_CURRENT_VERSION` from its `contractVersion` property; mismatches and duplicate IDs fail before generation. The revision is a load-time compatibility guard, not a marketed plugin generation. Loading uses an isolated dependency resolver and atomic batch registration. C++ plugins register `ICppTypeLowering`, `ICppCallableLowering`, or `ICppArtifactContributor`; C# post-analysis output can register `IBindingEmitter`. Stateful lowerings must implement `ICacheFingerprintProvider`. See the [final lowering architecture](lowering.md) for recipes, plugins, shims, and safety policy.

## Mappings and policies

Use mappings only for facts that cannot be inferred safely:

- native/managed naming;
- opaque or unexposed types;
- string encoding and ownership;
- pointer/count relationships that names cannot identify;
- constructors and member-style functions;
- explicit template instantiations and C++ lowerings.

A mapping must not hide ABI uncertainty. If a non-trivial C++ type crosses a boundary, generate a C bridge instead.

BGCS's default C# mappings contain C/C++ standard type names only. Native-SDK aliases and constructor expressions belong in the consuming project's config, not in BGCS core. For example, use `"typeMappings": { "Uint8": "byte" }` for a project typedef and `"knownDefaultValueNames": { "ExternalPoint(1,2)": "new Point2(1, 2)" }` for its default expression. The latter is an exact expression mapping; it does not infer the ABI or layout of `ExternalPoint`.

## Strict safety diagnostics

`strictSafety` defaults to `true`, and `strictSafetySeverity` defaults to `SuppressFriendly`: unresolved semantics keep the raw ABI but remove that function's inferred friendly overloads. `Warning` deliberately keeps those overloads while reporting risk; `Error` makes validate/generate/build fail before commit. Diagnostics use `BGCS-SAFETY-*` codes and include the exact minimum `marshallingMappings` path. Set `strictSafety=false` only when an external audit owns those semantics.

When `typeMappings` redirects a native record to a project-supplied managed value type, add an `externalTypeContracts` entry. `NativeTypes` and `ManagedTypes` are ordinal selectors that accept `*` and `?`, so one audited contract can cover closed generic carriers such as `NativeVector_*` to `NativeVector<*>`. Every selected `typeMappings` pair is validated and overlapping contracts are rejected. `ByValuePolicy=Reject` permits pointer-only use, `RequireLayoutMatch` accepts by-value use only when parsed native size/alignment match the declared carrier, and `BypassLayoutValidation` explicitly continues without that proof. Accepted by-value carriers remain visible in Binding IR and emit `BGCS-SAFETY-EXTERNAL-TYPE`; the project must keep managed-layout and native-invocation tests.

```json
{
  "typeMappings": {
    "NativeVec2": "Vector2"
  },
  "usings": [
    "System.Numerics"
  ],
  "externalTypeContracts": [
    {
      "NativeTypes": [
        "NativeVec2"
      ],
      "ManagedTypes": [
        "Vector2"
      ],
      "Size": 8,
      "Alignment": 4,
      "ByValuePolicy": "RequireLayoutMatch"
    }
  ]
}
```

For C++ bridges, `loweringSafetyPolicy` defaults to `VerifiedOnly`. Use `AllowUserAsserted` for reviewed project recipes/plugins/shims, and `AllowUnsafe` only when the project explicitly owns ABI and lifetime risk. The latter continues generation but emits the auditable `BGCS-SAFETY-LOWERING-BYPASS` diagnostic.

## Ownership and buffer marshalling

Use `marshallingMappings` when pointer syntax cannot express ownership or buffer relationships:

```json
{
  "marshallingMappings": {
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
  "variadicFunctionVariants": {
    "native_log": [
      {
        "Suffix": "IntString",
        "ParameterTypes": [
          "int",
          "byte*"
        ],
        "ParameterNames": [
          "value",
          "text"
        ]
      }
    ]
  }
}
```

C default argument promotion must already be reflected in the configured types: use `double` rather than `float`, and `int` rather than narrow integer types. Each variant uses the original native EntryPoint.

## Base configuration

```json
{
  "baseConfig": {
    "Url": "file://shared.windows-x64.json",
    "IgnoredProperties": [
      "EntryFiles",
      "OutputPath"
    ]
  }
}
```

Relative base files resolve from the referring config. Circular references fail with a clear error. Loading an existing config never rewrites it.

## Presets

Presets are composable and generic: choose one target preset (`host-c`, `host-cpp`, `windows-c`, `windows-cpp`, `linux-c`, `linux-cpp`, `macos-c`, `macos-cpp`, `emscripten-c`, or `emscripten-cpp`) and optionally add API/output policies such as `c-library`, `function-table`, and `opaque-callbacks`. Library-specific facts stay in the consuming project's configuration rather than in BindGen-CS core.

```json
{
  "preset": "host-c,c-library,opaque-callbacks",
  "entryFiles": [
    "vendor/SDL/include/SDL3/SDL.h"
  ],
  "includeFolders": [
    "vendor/SDL/include"
  ]
}
```

Explicit project settings override preset defaults, independent of preset order.

## Workspaces

A workspace stores multiple configuration paths for repository-level automation:

```json
{
  "Configs": [
    "cimgui.json",
    "sdl3.json",
    "bgfx.json"
  ]
}
```

```bash
bindgen-cs workspace validate native/bindings/workspace.json
bindgen-cs workspace generate native/bindings/workspace.json
bindgen-cs workspace diff native/bindings/workspace.json
```

`generate` uses each config's `outputPath`; `diff` checks that same path without replacing the checked-in output. With single-file output enabled, a workspace does not introduce target-specific source directories.

The generated header records the `ABI reference target` used for native parsing. This is a marker, not a cross-platform certification: native headers may expose different declarations or layouts after target-specific preprocessing. Run ABI and native-consumer tests on every intended target before sharing one binding source.


C++ bridge IncludeFolders, SystemIncludeFolders, TargetSysRoot and CompilerPath expand environment variables before resolving paths relative to the configuration directory. Cross-target bridge profiles must supply their actual SDK/sysroot and must not discover host C++ headers.
