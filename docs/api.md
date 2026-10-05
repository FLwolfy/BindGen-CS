# BindGen-CS API reference

[Documentation](README.md) · [Architecture](architecture.md) · [Configuration](configuration-guide.md)

This page describes the current source contract. Configuration properties and operation results
use camelCase; methods, types and enum values use PascalCase. Generated native names follow the
binding configuration.

## Generate bindings

`BGCS.Facade.BindingGenerator.Generate(configPath, outputPath?)` loads a composed configuration
and returns a `BindingGenerationResult<BindingModule>`. Use this entry when no per-instance
extension or live progress subscription is needed.

`CsCodeGenerator` owns a reusable generation configuration and operation diagnostics:

| Entry | Behavior |
| --- | --- |
| `Create(configPath)` | Loads, composes and validates file-backed configuration |
| `GenerateConfigured(outputPath?)` | Generates the configured entry files; relative paths use the configuration directory |
| `Generate(...)` | Accepts explicit header lists and, optionally, caller-provided parser options |
| `AnalyzeConfigured()` | Produces frozen Binding IR and diagnostics without replacing generated output |
| `lastResult` | Describes the current completed attempt; null before completion or after a preflight exception |
| `settings` | Exposes the configuration used by subsequent attempts |
| `patchEngine` | Registers bounded preprocessing or staged source patches |
| `PostConfigure` | Reports per-attempt configuration before preprocessing; subscribers own their subscription |
| `GetMetadata()` / `SaveMetadata(path)` | Inspect or write the current attempt's tooling metadata |
| `Reset()` | Clears attempt state without discarding configuration or subscribers |

Every attempt starts a new diagnostic lifetime. A failed request cannot expose a prior successful
`lastResult`. Failure before output commit preserves the previous complete output directory.
The generator runs on one owner thread; do not mutate configuration or start a second request
on that instance while it is executing.

```csharp
using System;
using BGCS.Facade;
using BGCS.Core.Logging;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
generator.LogEvent += Report;
try
{
    if (!generator.GenerateConfigured())
    {
        foreach (var diagnostic in generator.lastResult!.diagnostics)
            Console.Error.WriteLine(diagnostic.message);
    }
}
finally
{
    generator.LogEvent -= Report;
}

static void Report(
    LogSeverity severity,
    string message
) => Console.WriteLine($"{severity}: {message}");
```

The library does not attach a console. `logLevel` and `cppLogLevel` control live notifications;
the immutable result still retains diagnostics below those thresholds. `messages` is a read-only
view of the current attempt. Copy it when retaining it across requests.

## Configuration and programmatic input

Use `ConfigLoader`, `ConfigValidator` and `PresetResolver` to compose a configuration. Important
settings include `entryFiles`, `outputPath`, `targetId`, `targetTriple`, `targetSysRoot`,
`compilerPath`, `parserKind`, `importType`, `marshallingMappings` and `runtimeNamespace`.
The CLI schema command describes the complete installed configuration contract.

```csharp
using BGCS.Configuration;
using BGCS.Facade;
using BGCS.CppAst.Parsing;

var config = new CsCodeGeneratorConfig
{
    apiName = "NativeApi",
    @namespace = "Sample.Generated",
    libName = "sample",
    parserKind = CppParserKind.C,
    importType = ImportType.LibraryImport,
    generateExtensions = false
};
var generator = new CsCodeGenerator(config);
bool success = generator.Generate("include/api.h", "Generated");
```

## Core collections and text

`TrieSet<T>` owns copied sequence keys and returns read-only sequences. `TrieStringSet`
uses that implementation for character keys; matching does not expose mutable trie nodes.
Both support empty keys, shortest/longest prefix lookup, prefix enumeration, removal, and clear.
An unmatched lookup returns `false`; it never reports a zero-length match unless the empty key exists.

`ConcurrentList<T>` serializes individual operations. Enumeration captures an independent
snapshot, so a later mutation cannot invalidate it. Use `syncObject` to make a caller's
compound workflow atomic; enumerators do not retain the lock.

`WordList` reads and writes deterministic UTF-8 text or compressed, length-prefixed UTF-16
vocabulary. Read operations retain the caller's stream ownership and merge only after the
complete input passes validation. Writers prepare a sibling candidate and install it after
flushing. Character comparison is invariant across cultures.

`FileNameHelper.SanitizeFileName` uses the same leaf-name rules on every host: reserved
characters and Windows device names are normalized, trailing dots/spaces are removed, and
the result is limited to 255 UTF-16 units and 255 UTF-8 bytes without splitting a scalar.
An optional directory prefix is preserved; this API does not authorize or contain a path.

## Authoring language processing

`BGCS.Language.CSharp.Preprocessing.CSharpPreprocessor` evaluates C# conditional directives
using the same Roslyn frontend already used by C# generation. It owns the initial symbol
snapshot; each source file's `#define` state remains local to that invocation. Removed
directives and excluded code become spaces, preserving UTF-16 positions and CR/LF characters.
Invalid/unbalanced directives and active `#error` fail with `FormatException` and a source location.
It does not perform C/C++ preprocessing, which belongs to the Clang frontend.

`ParserContext.scopeDepth` exposes scope balance without granting access to the mutable stack.
All analyzer dispatch, including nested scopes, rejects a successful result that does not
advance the cursor. Syntax-node child lists are owned; their public view cannot mutate storage.

## Frozen analysis and emission

`BGCS.Intermediate` owns the dependency-free semantic contract: `BindingModule`, types, functions,
delegates, constants, imports, `MarshallingPlan`, diagnostics and `BindingGenerationResult<TModule>`.
It also owns the frozen `CppBridgeModule` and `ICppBridgeEmitter` contracts.

The configured C# chain is:

```text
configuration → C/C++ parser → declaration analysis → Binding IR
→ safety and coverage validation → CSharpEmitter
→ staged patches → single-file composition / optional Runtime → atomic publication
```

`IBindingEmitter` consumes frozen IR and an `EmissionContext`; it does not receive Clang objects,
an SDK resolver or mutable generation services. `CSharpEmitter` rejects unsupported ABI semantics
with `BGCSCS001` before writing output. Alternate emitters depend on Intermediate.

## C++ bridge

`Cpp2CCodeGenerator.GenerateConfigured()` and the explicit `Generate(...)` entries produce a
`BindingGenerationResult<CppBridgeModule>` available through `lastResult`. The C++ chain is:

```text
C++ AST → analysis and registered lowerings → frozen Bridge IR
→ C header / C++ implementation → C parser → Binding IR → C# bindings
```

`ICppBridgeEmitter` consumes the frozen bridge model. AST interpretation and lowering belong to
analysis. An unsupported declaration reports `BGCSCPP001`; a file-system publication failure
reports `BGCSIO001`. Unknown declarations are not silently treated as blittable values.

`CppBridgeBuildManifest` describes sources, includes, defines, native libraries and the selected
target. Native build providers turn it into a shell-independent process plan. Export inspection
and `NativeAssetLayout` validate the actual binary before packaging it under `runtimes/<rid>/native/`.

## Patches and metadata

### Mutable overload analysis

`CsType`, `CsParameterInfo`, `CsFunctionVariation`, and `CsFunctionOverload` are mutable analysis
descriptors. Their native AST references belong to the current analysis invocation. They are not the
frozen emitter input. `Clone()` copies managed descriptors and collection containers while retaining
the source AST reference. `CsType.Clone()` preserves `in` as well as `ref` and `out` classification.

`ValueVariation` captures independent parameter projections for signature comparison. Its hash does
not change when the original descriptors change; its `parameters` property returns copies. A
default-initialized value can be compared and hashed safely. `TryUpdateVariation` rejects an absent
old variation without adding the replacement. Accepted analysis descriptors should be changed through
the overload planner's update workflow so the variation collection and signature set remain coherent.

`IPrePatch.Apply(config, result)` operates on the current mutable configuration and borrowed
`ParseResult` before analysis. It does not rewrite input headers. AST references must not escape
the attempt. `IPostPatch` receives current-run metadata and the complete staged output tree.
`PatchContext.ReadFile` / `WriteFile` accept relative paths within that boundary and reject
escaping paths or reparse points. New files participate in the same final publication.
Post-patches run before single-file composition and optional standalone Runtime emission.
Semantic additions shared by emitters belong in analysis or IR instead of a second generation path.

## Runtime ownership

### Attempt metadata

`CsCodeGenerator.GetMetadata()` returns the live mutable metadata for the current attempt. A later
attempt or `Reset()` replaces that container. `SaveMetadata(path)` is the generator's metadata
inspection writer. The unused `CsCodeGeneratorMetadata.Save` / `Load` file entry points were removed;
the metadata container owns named contributions rather than an independent persistence workflow.

Merging a previously absent contribution clones it according to that entry's policy. Optional
function-table contributions participate only when `BGCS.Configuration.MergeOptions.mergeFunctionTable`
is enabled. A function-table merge validates the complete incoming batch before appending copied
entries, so conflicting assignments leave the destination unchanged. Dictionary clones preserve
their key comparer, constant clones preserve carrier overrides, and typed list clones retain nulls.

### Language boundaries

`BGCS.Language.Lexing` owns source-backed tokens and numeric syntax classification.
`BGCS.Language.Parsing` owns cursors, analyzer dispatch, and parser results.
`BGCS.Language.Syntax` owns shared syntax-tree containers, while
`BGCS.Language.Diagnostics` owns source positions and language diagnostics.
Dialect nodes and analyzers remain under `CSharp` and `Cpp`.

The unused analyzer-free `BGCS.Language.Cpp.CppParser` and unused C# expression placeholder were
removed. Native declarations are parsed by `BGCS.CppAst.Parsing.CppParser`; supported macro
expressions use `BGCS.Language.Cpp.CppMacroParser`. These have different responsibilities.

The lexer preserves adjacent source ranges, empty quoted strings and comments, and start positions
across LF, CRLF, and CR. Numeric syntax is classified once by the lexical boundary. The generator's
numeric projection reuses it and additionally selects managed carriers for unsuffixed integer values.

`BGCS.Runtime` has no dependency on the generator, parser or consumer application.

- `FunctionTable` distinguishes borrowed pointer storage from owned storage and `INativeContext`.
  Disposal releases only owned resources and rejects further operations. Failed table initialization
  disposes its candidate context before publication.
- `NativeCallback<T>`, `NativeCallbackRegistry<TKey,TDelegate>` and
  `NativeCallbackRegistration<TDelegate>` retain callbacks through the native registration lifetime.
  Registration disposal coordinates in-flight calls and permits an explicit unregister retry.
- `NativeAsyncOperation<TResult>` owns completion state and reports terminal cleanup failures.
- `NativeCallbackExceptionBoundary` prevents managed exceptions from escaping an unmanaged callback.
- `NativeAotCallback` accepts static unmanaged thunks for AOT consumers.
- `Atomic<T>` supports unmanaged integers up to 64 bits. Arithmetic uses unchecked overflow;
  copying the wrapper creates separate storage. Larger representations fail before access.
- `Utils` provides matched unmanaged allocation/free and native text conversion. UTF-16 allocation
  includes its terminator; allocation sizes and array byte counts are checked before use.

Ownership, allocator pairs, callback threading and asynchronous completion are explicit mapping
contracts. Safety diagnostics report missing high-risk semantics.

## Verification

See [Testing](testing.md) for managed, native, Wasm and NativeAOT consumers. The CLI's `build`
command generates and compiles C#; native invocation requires a real target binary and consumer.
BGCS acceptance uses its own fixtures and remains independent of any engine integration.
