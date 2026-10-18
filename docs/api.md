# BGCS 3.0 public API

[Documentation](README.md) · [简体中文](api.cn.md) · [Configuration](configuration-guide.md) · [Extensibility](extensibility.md)

The embedded SDK uses `BGCS.BindingHost` and immutable requests. CLI,
workspace and MSBuild invoke the same Host pipeline. Generation requires
.NET 10. Properties use camelCase; types and methods use PascalCase.
`bindgen-cs version --json` reports separate product, Contracts, model and
worker protocol identities.

## Host composition and ownership

```csharp
using System;
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
```

The default registers built-in implementations, including the isolated Clang
worker client. Composition freezes when the Host is built; session services
and plugin instances are independently owned. Factories must not return one
mutable object to unrelated sessions.

```csharp
await using BindingHost host = BindingHost.CreateBuilder()
    .AddBuiltIns()
    .WithWorkerAssetRoot("/absolute/path/to/deployed/worker")
    .WithParseTimeout(TimeSpan.FromSeconds(300))
    .AddPlugin<MyPlugin>()
    .Build();
```

`MyPlugin` implements `BGCS.Contracts.Plugins.IBindingPlugin`. Ordinary
plugins reference Contracts only. Worker root configuration is optional when
SDK deployment is available. Parser resources match the **generator host**,
even when the native target differs.

`DisposeAsync()` stops new sessions, cancels active work and waits for session
services to release. Request cancellation remains independent. The Host owns
generation; generated library contexts own native invocation.

## Capture configuration and generate

```csharp
GenerationRequest request = await GenerationRequest.FromFileAsync(
    "bindings/bindgen.json", cancellationToken: cancellationToken);
GenerationResult generated = await host.GenerateAsync(request, cancellationToken);
if (!generated.success)
    throw new InvalidOperationException(generated.GetDiagnosticSummary());
```

`FromFileAsync(path, profile: null, cancellationToken: token)` validates grouped versioned configuration and
captures its input closure. Each declaring document owns its relative paths;
a later process working-directory change cannot change a captured request.
The synchronous `FromFile` entry accepts the same optional cancellation and
configuration-loader arguments. Cancellation covers configuration composition
as well as later generation when the token is passed to both operations.

Only an explicit HTTPS `extends` object with `url`, pinned `sha256` and a local
`pathBase` can fetch a remote base. Its URI and content identity are recorded
separately from filesystem inputs. A caller can supply `configurationLoader:
new BGCS.Configuration.BindingConfigurationLoader(httpClient)` for its own transport; the caller
owns that `HttpClient`. See the [composition rules](configuration-guide.md).

SDK-created configuration uses `new GenerationRequest(configuration, baseDirectory)`,
which validates and defensively freezes input. Immutable overrides retain the
original configuration provenance:

```csharp
GenerationRequest alternative = request.WithConfiguration(
    request.configuration with
    {
        output = request.configuration.output with { root = "Generated/Alternative" }
    });
```

The groups are `library`, `source`, `targets`, `api`, `semantics`, `cpp`,
`runtime`, `output`, `extensions`, `cache`
and `execution`, with `extends` and profiles for composition. C/C++ use the
same request. See the [generated reference](generated/config-reference.md)
for exact constraints and defaults.

`GenerateAsync` parses/verifies detached facts; C++ additionally lowers a
bridge, emits and reparses its header, and verifies the bridge contract.
Semantic and ABI analysis, managed projection, emission, processors and final
emitted-contract checks precede complete candidate publication. Generation
alone does not certify native linking or runtime invocation.

## Generation results

`GenerationResult` exposes `success`, `published`, `outputRoot`, `artifacts`, `cacheStages`, `targetModels` and `diagnostics`. Complete owned output is hash-checked and published as one collection. Consumer compilation, native compilation/linking and invocation belong to external builds and acceptance; generation does not certify them.

## Analysis, inspection, comparison and bridge-only output

| Method | Result and purpose |
| --- | --- |
| `AnalyzeAsync(AnalysisRequest, token)` | `AnalysisResult`: native module, resolved target, parser dependency and argument-input snapshots, diagnostics. |
| `InspectAsync(GenerationRequest, token)` | `InspectionResult`: target native, optional bridge, interop and managed models without replacing formal output. |
| `DiffAsync(GenerationRequest, token)` | `GenerationDiffResult`: Added/Removed/Changed paths and previous/current digests. |
| `BridgeAsync(GenerationRequest, token)` | `GenerationResult`: native bridge without requiring managed projection of otherwise valid native exports. |

```csharp
using BGCS.Contracts.Inputs;

AnalysisResult analysis = await host.AnalyzeAsync(
    AnalysisRequest.FromFiles(["include/native.h"], SourceLanguage.C),
    cancellationToken);
GenerationDiffResult difference = await host.DiffAsync(request, cancellationToken);
```

Analysis supports explicit target/toolchain, standard, header search inputs,
defines, arguments, preamble/postamble and parse options. Detached models
contain no Clang handles. Analysis is not a native build.

Each inspected target also retains its `resolvedTarget` descriptor with the
actual pinned compiler/SDK configuration. Environment inspection uses these
Host-resolved facts rather than inferring a new toolchain from target-ID text.

Diff verifies owned previous output under a publication lock. Unowned,
incomplete or tampered sets fail. Formal output remains untouched.
`hasChanges` describes logical artifacts, rather than operational cache decisions.

## Workspaces

```csharp
BindingWorkspace workspace = new WorkspaceLoader().Load("workspace.json");
WorkspaceResult generated = await host.GenerateWorkspaceAsync(workspace, cancellationToken);
WorkspaceDiffResult compared = await host.DiffWorkspaceAsync(workspace, cancellationToken);
```

Each project retains its own configuration base, profile, targets and input
identity. Graph construction checks unique IDs, prerequisites, cycles and
output/input conflicts. Default parallelism is two. Results preserve project
order and Completed/Failed/Skipped status. Failed prerequisites block
dependents; independent branches retain their results. Overall success
requires all projects to succeed. Execution does not change the process
working directory.

## Extension contracts

The builder supports factories for frontend, managed projection and backend,
and registrations for target providers, native/interop transforms, C++ type
and callable lowerings, processors and validators.

`AddPlugin<T>()`, `AddPluginFactory(...)` and `AddPluginManifest(...)` configure
neutral plugins. Metadata declares Contracts compatibility, exact service IDs,
ports, priorities and settings schema. Discovery and session instances have
separate ownership. Duplicate IDs and changed activation descriptors fail.
Settings are independently cloned and validated.

`BGCS.Contracts.Plugins.ICacheFingerprintProvider` captures exact private
state and external inputs. An extension without complete fingerprints can run,
but affected caching is disabled. Candidate processors execute on eligible
emission-cache hits, before final source-contract validation and publication.
Use controlled candidate artifacts rather than writing into formal output.
See [extensibility](extensibility.md) and the
[neutral example](../examples/LoweringPlugin/README.md).

```text
NativeModule → CppBridgeModule (C++ only) → InteropModule
→ ManagedApiModel → backend emission plan → candidate artifacts
```

The C# plan belongs to its backend. Neutral models contain no frontend AST,
service instance or physical output directory.

## Owned output and worker resources

`BindingArtifactManifest.Read(outputRoot, libraryId, token)` locks and verifies
the complete owned set before returning descriptors. Integrations use exact
manifest entries instead of directory globs.

`host.ClearOwnedOutputAsync(request, token)` clears verified owned output,
protects inputs/cache/plugin assets and retains an empty ownership marker
for rebuilding. Wrong owners, unlisted files and tampered data fail.

`WorkerAssetDeployment.Deploy(sourceRoot, manifestPath, destinationRoot, token)`
deploys a locked host parser closure. `Clear(...)` preserves valid ownership
for redeployment. SDK targets use these APIs; users do not assemble parser
resources manually. Process isolation is not an untrusted-plugin sandbox.

## Consumer API and Runtime

### Members on value carriers

`api.memberFunctions[].kind = "value"` places an explicitly selected C operation
on the existing generated record. The receiver is the caller's original value;
there is no native factory, destructor, hidden context field or global library
selection. Rename the carrier with `api.records`, rather than `objectName`.

For `int value_add(NativeValue* self, int delta)`, declare both presentation and
the native one-value borrow:

```json
{
  "api": {
    "memberFunctions": [
      { "function": "value_add", "nativeType": "NativeValue", "receiver": "self", "kind": "value", "name": "Add" }
    ]
  },
  "semantics": {
    "valueParameters": [
      { "function": "value_add", "parameter": "self", "direction": "inputOutput", "lifetime": "call", "retainsPointer": false, "allowsNull": false }
    ]
  }
}
```

```csharp
NativeValue value = default;
int result = value.Add(api, 2); // forwards to api.Add(ref value, 2)
```

An input-only receiver produces a `readonly` member forwarding `in this`.
The context method pins original storage and validates native alignment for the
complete call, including other `in`/`ref` values and spans that overlap it.
The same value can use different independent binding contexts; a disposed
context rejects new calls. In explicit static API mode, a member takes the
concrete `ApiContext`, while the existing static `Api.Add(ref value, ...)`
operation remains available. No field, size, offset or raw import changes.

This finite projection requires complete representable record storage, an
explicit nonretaining call borrow and a supported direct scalar/void result.
Output-only, volatile/unknown-qualified, opaque, flexible-array and over-aligned
receivers retain their raw API with a stated projection omission. Status,
ownership-return, callback and async combinations need their own checked
projection. A value receiver cannot declare `objectName` or `releaseDomain`.

Local Windows x64 CoreCLR tests exercise all three imports and Runtime modes,
including native address identity while a call spans compacting GC. NativeAOT,
browser Wasm, other systems and final package certification remain separate.

### Context and runtime

The default generated API is an independent instance with exact raw imports
and eligible friendly wrappers. Dynamic targets provide `Load(...)`; supported
static targets provide `FromStatic()`. Custom providers/external tables have
explicit factories. Factories follow runtime/import capabilities.

Consumers reference Runtime or request Runtime source output. Runtime does
not require the generator/parser. A generated context retains a
`NativeModuleSession`; calls, owned objects, callbacks and async operations
retain leases. Closing stops new public calls while existing resources retain
native cleanup access. Module unload follows the final lease release.

Friendly strings, buffers, ownership and enums require explicit representable
semantics. Fixed-width enum configuration changes friendly presentation while
preserving raw scalar ABI. Unsupported by-value ABI or unknown ownership
produces diagnostics rather than fabricated safe wrappers.

See [Runtime lifetime](runtime-lifetime.md), [ABI safety](abi-safety.md),
[MSBuild](msbuild.md) and [testing](testing.md). Local receipts certify named
targets, runtimes, imports and stages. Release readiness requires every
mandatory same-revision gate.

## Explicit native addresses

`api.pointerCarriers` names an existing record pointer, opaque record pointer or
named `void*` alias. `pointerDepth` defaults to `1`; `readOnly: true` selects a
const first pointee. Use an inspected native pointer identity when a record name
would match multiple qualified shapes. No pointer facts or storage sizes are
invented.

An optional `documentation` string replaces the carrier's public description;
it is escaped as inert XML documentation. It cannot inject declarations, XML
includes or lifetime facts, and the generated non-owning warning remains.

```json
{
  "api": {
    "pointerCarriers": [
      { "nativeType": "NativeEngine", "name": "EngineAddress" },
      { "nativeType": "NativeWindow", "name": "WindowListAddress", "pointerDepth": 2 }
    ],
    "pointerFunctions": [
      { "function": "get_engine", "returns": "EngineAddress" },
      { "function": "engine_open", "parameters": [{ "parameter": "engine", "carrier": "EngineAddress" }] }
    ]
  }
}
```

An address carrier contains one `nint`, exposes `Handle`, null checks and address
equality, and has no ownership or disposal behavior. Its raw imports retain the
original pointer ABI. Explicit pointer-function presentation can reuse separately
proved text, scalar, buffer and value conversions. It cannot replace an owned,
module-borrowed or managed text result; those use their existing leased API.

Complete native records can expose `DangerousField_<name>()` references and
`DangerousArray_<name>()` fixed-array views. Pointer fields expose values rather
than references reinterpreted as wrapper structs. `DangerousGetAddressElement`
handles an exact lower pointer carrier, including opaque pointer tables.
Const storage uses readonly references/spans; writable whole-record and typed
pointer views are omitted when they could expose const storage. Unmodeled volatile
storage has no convenience view. Opaque addresses have no invented dereference.

Every `Dangerous` view requires the caller to keep the native allocation live,
provide the correct bounds and synchronize native access. Null, alignment and
checked offset tests do not establish these conditions. The view has no module
lease; keep the appropriate context/resource owner alive independently. Unknown
lifetime diagnostics remain subject to the configured safety policy. This
presentation alone does not certify a platform or a lifetime protocol.
