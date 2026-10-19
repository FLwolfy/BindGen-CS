# BGCS 3.0 neutral extensions

This guide describes the new Host implementation. See
[implementation status](implementation-status.cn.md) for outstanding work and release gates.

Standard plugins reference `BGCS.Contracts` and implement
`BGCS.Contracts.Plugins.IBindingPlugin`. Native facts, typed bridge plans, interop semantics
and candidate reads use neutral contracts without a Clang frontend dependency.

A descriptor declares the plugin ID, semantic version, required Contracts major/minor and
complete service collection. Service IDs are globally unique. Ordered transformation,
lowering and processor ports use descending priority and ordinal ID for ties. Target
providers are queried in ordinal ID order; multiple providers claiming the same target
are rejected rather than selected by priority. Frontend, projection and backend each
accept one primary implementation.

The Host creates a discovery instance, captures its descriptor and disposes it. Every
generation session owns an independent plugin. Configure registers factories before the
registry freezes; each requested factory must return an independent disposable service.
Services release before their plugin. A retained registry cannot accept later registrations.

```csharp
public sealed class DomainPlugin : IBindingPlugin
{
    public PluginDescriptor descriptor => new(
        "example.domain", "1.0.0",
        [new("example.domain-type", ExtensionPort.CppTypeLowering, 20)]);

    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration)
    {
        registry.RegisterFactory<ICppTypeLowering>(
            "example.domain-type", () => new DomainLowering());
    }
}
```

The executable Contracts-only lowering and validator fixture lives in
`tests/fixtures/plugins/NeutralLoweringPlugin`. `NeutralCppBridgeTests` exercises real
C++ construction, C-header reparse, managed compilation and native calls.

SDK applications use `AddPlugin<TPlugin>()` or `AddPluginFactory`. Explicit private
assembly deployment uses `AddPluginManifest`. Configuration accepts the same manifests
through `extensions.plugins`.

Use `IManagedApiTransform` with `ExtensionPort.ManagedApiTransform` to customize the
existing managed projection; direct SDK composition uses `AddManagedTransform`. It
receives an immutable `ManagedApiModel` after the primary projection and before C#
planning. For example, change enum item names through `ManagedType.memberNames` and
stable member IDs without replacing type, string, ownership or callback projection.
Only Contracts is required. The returned model must retain the identical `InteropModule`
instance; a managed presentation transform cannot silently replace the native ABI.

Global service IDs, priority order, session ownership, reverse disposal and fingerprint
rules apply to this port too. The Contracts-only example is
`tests/fixtures/plugins/NeutralLoweringPlugin/ManagedNamesPlugin.cs`.
`ManagedTransformTests` checks state-dependent caching, disposal and preservation of
the previous publication after failures. These repository checks do not replace clean
installation certification of the final packages.

`ISourceContributor` supplies generated inputs before the original C/C++ parse. It is
different from an artifact processor: its public headers can introduce declarations that
then pass through ordinary selection, analysis, lowering and C# generation. The same
Contracts-only plugin manifest is consumed by the SDK and CLI.
The port is currently covered by the implementation tests; the release and installation
certification status remains in the implementation status document.

```csharp
public PluginDescriptor descriptor => new(
    "example.generated", "1.0.0",
    [new("example.generated-inputs", ExtensionPort.SourceContributor)]);

public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration settings)
    => registry.RegisterFactory<ISourceContributor>(
        "example.generated-inputs", () => new GeneratedInputs());
```

The contributor receives a frozen `SourceContributionRequest`: the concrete target,
effective source inputs, library identity, managed/Runtime namespaces and selected `apiStyle`. Private candidate
and formal output directories are removed from its request. It returns complete immutable
bytes through `SourceContribution`; the Host makes its own defensive copies before staging.
An explicit `Declared` or `UserAsserted` provenance is required. Generated text is not
automatically promoted to verified native behavior.

Use the supplied API style when a managed contribution extends a generated partial API:
instance and static APIs require different declarations. A standalone managed helper can
instead use its own type name. Service IDs can use portable slash-separated components,
such as `example.generated/helpers`; the complete ID remains part of every logical input
identity and its owned output path.

| Role | Required language | Host-owned output |
|---|---|---|
| `PublicHeader` | `C` or `Cpp` | `native/include/contributions/<service>/<path>` |
| `NativeSource` | `C` or `Cpp` | `native/src/contributions/<service>/<path>` |
| `ManagedSource` | `None` | `managed/Extensions/<service>/<path>` |
| `Resource` | `None` | `native/resources/contributions/<service>/<path>` |

Set `exposeToBindings` on a public header to include its declarations as ordinary binding
entry declarations. An unexposed header can remain a dependency of an exposed header.
Native source suffixes and media types must agree with the explicit language. A C parse
cannot expose a C++ header. Resources are copied as declared bytes and never implicitly
compiled. Native sources can include a generated header with
`#include "contributions/<service>/<path>"` through the generated include root.

Logical native provenance uses `contributions/<service>/<path>` independently of the
physical staging directory. The Host records generated files, directory membership,
digests and native compilation inputs in the ordinary generation artifact manifests. A later
standalone native build consumes published, verified inputs after the temporary source
store has been released. Managed helpers enter the same candidate compilation and final
contract checks as other generated C#; single-file output composes them into `Bindings.cs`.
An invalid contribution or a failed final generation validation does not replace the previous
complete output set.

The contributor runs once for each original target parse, before parser-cache lookup;
bridge reparse does not invoke it again. Generated content participates in stage identity
even when a contributor declares an unchanged private fingerprint. Implement
`ICacheFingerprintProvider` and declare external inputs to enable eligible cache stages;
an unfingerprinted contributor disables affected emission reuse. Contribution services
receive the session cancellation token and are released after their bytes have been
captured, before parsing or publication. Portable service/file paths, case collisions,
role/media mismatches and strict UTF-8 are validated by the Host.

Each manifest selects one public entry type and assembly. Assets declare the complete
managed/native execution closure with SHA-256 digests; an existing `.deps.json` must also
be included. Paths cannot traverse or escape the manifest directory through links.
Changing locked inputs requires a new request or Host.

Clang-specific inspection uses `BGCS.Frontend.Clang.Advanced.IClangFactContributor`.
The frontend exports `ClangFrontend` and the three scoped Advanced entry types only.
Its mutable parser model and AST construction helpers are internal. One exact
`BGCS.Frontend.Clang.Tests` friend is registered in
`eng/architecture/test-friends.json` for implementation regressions; the architecture
gate rejects production friends, unregistered declarations and fake test assemblies.
This advanced interface requires the matching frontend reference supplied by the full SDK;
ordinary lowering, target, managed and artifact extensions continue to require only Contracts.
Its plugin still implements `IBindingPlugin`, but declares only
`ExtensionPort.FrontendFactContributor` services and registers the exact advanced interface:

```csharp
registry.RegisterFactory<IClangFactContributor>(
    "example.clang-facts", () => new DomainClangFacts());
```

The manifest must set `workerOnly: true` and `workerParserIdentity` to the exact parser
identity printed by `bindgen-cs version --json` (`parser.expectedIdentity`). The current
identity is `ClangSharp/20.1.2.4;LLVM/20`; updating the parser requires rebuilding and
revalidating the advanced plugin. The Host reads the locked descriptor and settings schema
without loading this implementation. A worker activates it in a private collectible load
context sharing Contracts and the exact worker-owned frontend/ClangSharp assembly identities.
Other dependencies remain part of the explicitly hashed private asset closure.

`Contribute(ClangFactContributionContext, NativeModule, CancellationToken)` runs
synchronously while the translation unit is alive. The context resolves an original stable
declaration ID to its borrowed `CXCursor`, maps original source locations and exposes the
borrowed translation unit. Return a complete detached `NativeModule` for the same target,
language, pointer width and byte order. Native access through the context fails after the
method returns, including when it throws. Copying a cursor or translation-unit handle does
not extend its lifetime; contributors must not retain those handles or use them asynchronously.
Standard downstream extensions receive only the detached model.

Contributors run on each parse. Read `context.request.purpose`, whose neutral values are
`SourceAnalysis` and `BridgeVerification`, rather than guessing
from temporary paths or file names. A source-only contributor should return the unchanged
module for verification requests. Plugin activation, declared input capture and fingerprint
checks still apply when contribution is skipped; generated bridge ABI verification retains
its independent checks.

Before an affected cache lookup, a separate worker discovers plugin/service fingerprints
and declared external inputs without parsing an AST. The parse worker repeats that capture,
compares it with preflight and checks it again after contribution. Publication rechecks the
discovered inputs. Both the plugin and its services must provide complete fingerprints for
eligible cache reuse; missing fingerprints allow execution and disable affected caches.
Declare files through `IExternalInputProvider` or the fingerprint contribution, and resolve
their paths using the frozen plugin settings. Exceptions, changed inputs, fingerprint drift,
worker crashes, cancellation and timeout fail the request without publishing partial output.

The executable advanced example is `tests/fixtures/plugins/ClangFactPlugin`.
`WorkerFactPluginTests` exercises real live-cursor contribution, scoped invalidation,
preflight, fingerprint/input changes and worker failure containment. These local tests do
not certify final packages or additional platforms; use the release acceptance receipts.

The collectible loader shares the Host's Contracts identity and framework assemblies.
Other dependencies must come from the declared private closure. Disposal requests unload;
references retained by application code can delay collection. Neither process nor assembly
isolation creates a security sandbox: plugins are explicitly selected executable code.

Settings belong to `extensions.options[pluginId].values` and are validated against the
descriptor's `configurationSchema`. The default accepts an empty object only. Schemas use
Draft 2020-12 with bundled definitions, local anchors and references. External schema
documents are never fetched implicitly. The settings wrapper captures its declaring
document directory in `pathBase`; SDK callers can set an explicit base.
Composed settings preserve a JSON-pointer origin for each value. Plugins use
`ResolveInputPath("/rules/file")` or `GetPathBase("/rules/file")` for a specific input.
Reading a common `pathBase` for settings from different directories fails explicitly;
receipts retain the individual origins.

`IGenerationValidator` runs after the managed/native checks required by the current request.
Its immutable request contains the resolved target, final ABI, exact artifacts and an
`IArtifactReader`. Reads check hashes, paths and strict UTF-8, with a default complete-read
limit of 256 MiB. Error diagnostics, exceptions, changing registration identity and cleanup
failure all block joint candidate publication.

The Host can reuse the original built-in frontend result and built-in C# emission when
their complete inputs are established. Frontend eligibility includes the verified worker
closure and pinned .NET host/runtime, parser resources, effective arguments, source contents,
include-search order and directory membership. Unsupported ambient/compiler inputs disable
reuse. Custom frontend/backend implementations and extensions without complete fingerprints
do not silently share the built-in entries. Reports record each actual stage's hit, miss or
not-used result. Transforms, processors and
all validations required by this request still run. See [cache configuration](configuration-guide.md#plugin-bootstrap-and-cache-policy).

`IArtifactProcessor` currently replaces or deletes declared managed text and adds
helpers inside the current target's managed directory through
an editor that closes after the stage. Updated digests follow the candidates; the built-in
C# backend rereads imports and storage after processing. A custom backend with processors
requires its own final validator. Single-file output recomposes additional C# helpers before
checking imports. Native artifacts are outside this managed editor surface. Use declared source contribution or a native shim instead.

Consumer compilation, native builds and packaging belong to external tools, not
product extension ports. Advanced worker plugins still need their declared exact parser
compatibility and execution closure. Service reuse must follow its explicit lifetime
and thread-safety contract; an interface alone does not establish acceptance.
