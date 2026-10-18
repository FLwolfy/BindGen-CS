# Configuration Guide

[Wiki](README.md) | [中文](configuration-guide.cn.md) | [Generated field reference](generated/config-reference.md)

BGCS 3.0 uses one strict grouped `bindgen.json` for C and C++. The generator, Runtime
and default consumers use .NET 10 / C# 14. Configuration values become an immutable
session input; services and parser handles do not live in that input. The complete
installed schema is authoritative for field names and defaults:

```bash
bindgen-cs schema bindgen.schema.json
bindgen-cs schema workspace.schema.json --kind workspace
bindgen-cs schema plugin.schema.json --kind plugin
```

The [English](generated/config-reference.md) and [Chinese](generated/config-reference.cn.md)
references are generated from the same actual CLI schema. `scripts/generate-config-docs.py`
requires a built tool, exports that schema into an owned temporary directory, and offers
`--check` to reject stale references. It does not infer behavior from legacy fixture names.

The schema rejects unknown built-in fields. Plugin options are accepted only in their
registered plugin namespace after bootstrap. There is no global unknown-field bypass.
Only `schemaVersion: 1` is accepted; old flat configuration and facade APIs are not read.
The schema validates documents and fragments, while final validation checks the fully
composed source, library identities, targets, semantics and execution limits.

## Defaults and a complete small request

| Decision | Default | Explicit alternatives |
| --- | --- | --- |
| C standard | `c17` | A supported declared source standard |
| C++ standard | `c++23` | A supported declared source standard |
| Native target | `host` resolved to a concrete ABI | Configured target family and SDK |
| API style | `instance` | Explicit shared `static` style |
| Import | `functionTable` | `dllImport`, `libraryImport` |
| Framework | `net10.0` | The current major's schema fixes this baseline |
| Runtime delivery | `package` | `source` |
| Managed layout | `split` | `singleFile` with default `Bindings.cs` |
| Cache policy | enabled | Disable explicitly; reuse still requires proven inputs |

```json
{
  "schemaVersion": 1,
  "library": { "id": "sample", "nativeName": "sample" },
  "source": {
    "language": "c",
    "languageStandard": "c17",
    "headers": ["include/sample.h"],
    "includeDirectories": ["include"]
  },
  "targets": [{ "id": "host" }],
  "api": {
    "namespace": "Example.Sample",
    "name": "SampleApi",
    "names": { "sample_add": "Add" }
  },
  "output": { "root": "Generated" }
}
```

`generate` produces validated managed artifacts and any required C++ bridge source.
The consumer compiles, links, packages and deploys it externally. Generation, consumer
compilation and actual native invocation are different evidence stages.

## Source and language facts

`source.headers` is the ordered root header collection. `includeDirectories` and
`systemIncludeDirectories` are ordered search roots, not interchangeable declaration
policies. `includeTransitivelyReferencedHeaders` keeps transitive declarations;
`includeSystemDeclarations` deliberately opts into system declaration retention.
`defines` and `arguments` must match the actual native build. `preamble` and `postamble`
are explicit parser inputs and participate in content identity.

`includeHeaderHelpers` defaults to `false` for C. Ordinary binding generation imports
external C functions and their aliases without requiring extra exports from an existing
library. Set it to `true` to include selected static, inline and direct-call-only C
helpers; BGCS then emits adapters that must be compiled into the same configured native
module as its external functions. Exact helper selectors and helper cleanup requirements
diagnose this opt-in when it is missing. Explicit `variadicVariants` keep their requested
fixed adapters independently. C++ uses its ordinary C++ bridge instead.

`parseMacros` and `parseComments` select whether those facts are retained. Turn them off
only when those outputs are unnecessary. An umbrella header can include the public
surface of a library without listing every child header. Source identities, typedefs,
qualifiers, nested arrays and calling conventions remain native facts until later analysis.

Keep formal output and cache outside explicit include/source roots. A header next to the
configuration needs no `includeDirectories: ["."]`. Use dedicated `include/`, `src/` and
`Generated/` directories when an explicit include root is needed. Inputs are read without
silently changing the process current directory.

## Targets, SDKs and managed runtime

Each `targets[]` entry has an explicit `id`, optional `triple` and `settings`. Settings
include `compilerPath`, `sysRoot`, normal and C++ system header paths, definitions and
parser arguments. A simple executable name remains a discovery request; a path belongs
to the declaring configuration's directory. Native target IDs describe OS, CPU and ABI;
NuGet RIDs describe asset selection and do not replace target facts.

The worker follows the **host**. Target headers, primitive widths, layout, calling ABI,
SDK facts follow the **target**. Clang parsing does not require a
Clang final compiler. Using MSVC or GNU still requires matching headers and target ABI.
Do not mix host standard-library headers into a cross-target request.

`api.runtime` can select `coreClr`, `nativeAot` or `browserWasm`; omitting it lets the
runtime resolver choose the appropriate target default. Managed runtime and import mode
participate in capability checks. Browser Wasm emits an explicit static export-table adapter for the consumer to
link, rather than assuming desktop dynamic loading. GNU and musl are distinct ABI identities. Android SDK/API levels and Apple device/simulator SDK requirements
cannot be proven from a short target or RID string.

Use `doctor`, `inspect` and the [capability evidence](capabilities.md) to review actual
resolved facts. Missing prerequisites are diagnosed; BGCS does not implicitly download
SDKs or compiler installations.

## API naming, imports and layout

`api.namespace`, `name` and `names` control managed identity independently of parsing.
Use exact native names or unambiguous qualified callable names for mappings. A C++
export-name override belongs to `cpp.callableLowerings`; changing only a managed name
must not change the native ABI.

All import modes consume the same checked interop contracts. FunctionTable loads the
complete required symbol set and enables explicit module providers and independent
instances. DllImport and LibraryImport use their supported import/marshalling capabilities;
LibraryImport validation also checks its generated source. Choosing a mode does not
bypass unsupported by-value layouts, callback rules or special native scalar ABI.

The instance context offers `Raw` plus eligible friendly methods. FunctionTable contexts
use `Load()` or `FromProvider(...)`; DllImport/LibraryImport contexts use `Create()`.
Explicit static style has shared lifetime semantics. Consumers must enable unsafe code
and compile the exact selected target's managed manifest entries.

`output.layout = split` separates API, imports, types, callbacks and helpers by role.
`singleFile` composes managed artifacts into `output.singleFileName`; it must be a safe
`.cs` filename, not a path. Native and metadata files retain their own manifest entries.
Generated output is a jointly owned complete set. Failed parsing, ABI validation,
compilation or publication must preserve the previous complete set.

`api.enums` declares managed enum presentation with an explicit fixed-width underlying
kind and decimal member values. `api.enumMappings` selects an exact native typedef,
enum or scalar name. The mapping must preserve width and signedness; `Raw` retains the
original native carrier. This example preserves a high bit in a C `uint64_t` typedef:

```json
{
  "api": {
    "enums": [{
      "name": "WideFlags",
      "underlyingType": "uInt64",
      "isFlags": true,
      "values": [{ "name": "HighBit", "value": "1152921504606846976" }]
    }],
    "enumMappings": { "bgcs_wide_flags": "WideFlags" }
  }
}
```

Use decimal strings to preserve all 64 bits across configuration serialization. An
omitted underlying kind, duplicate member, out-of-range value or incompatible carrier
fails validation; naming an enum does not prove native storage or calling compatibility.

## Existing consumer value types

`api.externalTypes` maps an exact native struct to a closed consumer type through
explicit field copies. For example:

```json
{
  "api": {
    "externalTypes": [{
      "nativeType": "NativeVector_int",
      "managedType": "Consumer.NativeVector<int>",
      "fields": { "size": "Size", "capacity": "Capacity", "data": "Data" }
    }],
    "projectionInputs": { "sources": ["ConsumerTypes.cs"] }
  }
}
```

Static Roslyn binding resolves complete public writable scalar fields from frozen
`api.projectionInputs` source/reference metadata. It does not run a consumer or measure
CLR offsets/alignment. Field order can differ: imports continue to use the
generated native carrier, with independently checked native calling ABI. An external
type is never substituted into an import merely because its size matches.

Fixed-width integer and floating-point fields preserve kind, signedness and width.
Data-address fields may use an unmanaged pointer, `nint` or `nuint`; declared pointer
kind and target width must match. Address copies support packed fields
without assuming alignment. They neither retain pointed-to storage nor transfer its
ownership. Keep the pointee alive yourself and use unsafe access only within its
actual lifetime. A same-size `long` is not an address declaration. Callable pointers,
overlapping fields, nested aggregates and unproven storage require a separate typed
adapter. Field copies cannot preserve original `in`/`ref`/`out` address aliases.

## Strings, buffers, owned results and evidence

The pointer spelling `char*` does not establish UTF-8, termination, retention or ownership.
`wchar_t` width comes from the target; 2-byte and 4-byte text use different codecs.
A complete string assertion can be expressed as:

```json
{
  "semantics": {
    "stringParameters": [{
      "function": "sample_set_name",
      "parameter": "name",
      "encoding": "utf8",
      "codeUnitWidth": 1,
      "byteOrder": "native",
      "lengthUnit": "codeUnits",
      "direction": "input",
      "lifetime": "call",
      "zeroTerminated": true,
      "retainsPointer": false,
      "allowsNull": false
    }]
  }
}
```

For bounded input buffers, use `semantics.bufferParameters` with the exact parameter,
its `lengthParameter`, `lengthUnit`, direction, lifetime, retention and null policy.
Optional `requiredAlignment` must agree with native storage requirements. A span wrapper
is appropriate only for the declared access window; a retained native pointer needs a
separate lifetime owner. Length in bytes, code units, Unicode scalars and typed elements
must not be substituted for each other.

Writable capacity protocols can instead select `capacityParameter` and a distinct
`writtenCountParameter` naming one output integral pointer. The generated signature
accepts `Span<T>` and exposes `out int written` in **elements**. BGCS converts the span
capacity to the declared native units with checked arithmetic, then rejects a negative,
over-capacity or incomplete-element native count before exposing it. A capacity and
written count share the declared `lengthUnit`. Distinct initialized input length and
writable capacity require an explicit adapter; they are not silently conflated.

Use `semantics.valueParameters` for a single required pointer with an explicit
`direction` (`input`, `output`, `inputOutput`), `lifetime: "call"`,
`retainsPointer: false` and `allowsNull: false`. Supported scalars and checked generated
record carriers become `in T`, `out T` or `ref T`. The original storage stays pinned
through the call and its actual address alignment is checked before native code runs.
Aliased references, record/field overlap and overlap with a borrowed span preserve
their real addresses and write-through order. An `out` borrow does not first zero
storage that may also be an input. External field-copy projections cannot promise
this reference identity, so that convenience is omitted with a diagnostic.
Validated written counts are converted copies and must not overlap borrowed
value/span storage or another count output; overlapping requests fail before the call.
Raw imports keep their original pointers. An explicitly annotated one-byte native
`_Bool` uses `in bool`, `out bool` or `ref bool` while its raw pointer remains `byte*`.
The checked facade pins the original Boolean storage, preserving aliases and writes
even if a later result check throws. Its native and requested alignments must both
be one byte. Integer conventions such as Windows `BOOL`, unknown Boolean layouts,
nullable, retained, asynchronous, over-aligned, enum and pointer-to-pointer
conveniences require an explicit supported adapter. C++ bool-pointer lowering
remains a separate native bridge capability; equal size does not justify a C++
pointer reinterpretation.
Reference modifiers are preserved by aliases, function views and shared facades.
These wrappers cannot prevent native code from writing outside a supplied capacity;
the native library must obey the declared access and lifetime contract.

An optional pointer can instead be permanently bound to NULL in a checked overload.
Declare its `semantics.valueParameters` contract with `allowsNull: true`,
`lifetime: "call"`, `retainsPointer: false` and its actual access direction, then add
an `api.functions[].defaults` entry such as
`{ "parameter": "p_open", "value": { "kind": "null" } }`.
BGCS hides that pointer only in the NULL-bound facade and checks the remaining
string, value and buffer inputs normally. Other explicit defaults can generate
further overloads; aliases, named views and shared facades use the same operation.
This does not create a general nullable `ref T` overload: the complete pointer
signature remains available in `Raw`. Missing nullability, retention or lifetime
facts cannot be supplied by a NULL default alone.

`api.records[].constructorFields` selects a managed data-carrier constructor. An
exact fixed primitive array field also receives an additive `ReadOnlySpan<T>` copy
overload. Every selected array must have exactly its native element count; all
lengths are checked before any field assignment. The constructor copies into the
record's independent inline storage, and preserves the constructor accepting its
raw array carriers. This does not construct a native C++ object or lend a Span over
an owned native object. Pointer, Boolean, enum, record and special scalar arrays keep
their raw constructor with an explicit restriction on the Span convenience.

`semantics.ownedResults` identifies a result's matching `releaseFunction`, release/allocation
domain and `releaseThreadDomain`. Use `any` only when cleanup is actually thread independent;
`unknown` prevents assuming unrestricted automatic cleanup. Native construction and
release must use the same domain. An owned object retains the module needed by its destructor.
The [NativeShim example](../examples/NativeShim/README.md) shows explicit raw cleanup
when no automatic ownership contract has been provided.

Configuration assertions are `UserAsserted`. Declared source facts and independently
verified evidence retain their distinct levels. A user assertion never becomes `Verified`
merely because it was accepted. Unknown friendly semantics suppress unsafe conveniences;
a representable raw ABI can remain available. Unsupported ABI itself is a failure.
Equal sizes do not prove field offsets, alignment or by-value register classification.
Special scalars such as `long double` need supported evidence or pointer/shim paths.

Retained callbacks require an unregister-and-quiescence protocol. Async cancellation
requires a terminal native acknowledgement before releasing pinned memory or module leases.
These facts can be contributed through neutral interop ports; no config name invents an
unimplemented safety guarantee. Consult the actual feature inventory and tests before
relying on a particular callback or async projection.

## C++ typed recipes, templates and shims

The `cpp` group has `typeLowerings`, `callableLowerings`, `templateInstantiations`,
`nativeShims` and `virtualCallbacks`. C++ lowering builds typed bridge operations, emits
the C header/C++ source, reparses the actual C header and verifies exported contracts.
Its ownership and conversion semantics survive that reparse through stable identities.

Type recipes specify a matched native type, its exact C ABI carrier, typed parameter/result
conversions and optional explicitly fingerprinted helper fragments. Managed projections
add a public managed type and conversion assertions while keeping raw carriers exact.
Every configured managed expression is syntax validated even if its type pattern is unused.
Target-sized carriers such as `size_t` follow the actual target facts, not the generator host.

Callable recipes select qualified names and can override export names, exclude a declaration
or wrap the independently planned invocation. `templateInstantiations` selects concrete class
and fixed function templates. A native shim declares public C headers, implementation sources
and exact symbols, then uses ordinary parse/build/publication stages. Abstract virtual callback
interfaces require explicit finite callback slots and a native proxy lifetime protocol.

[The C++ cookbook](cpp-extension-cookbook.md), [lowering architecture](lowering.md),
[Contracts-only plugin](../examples/LoweringPlugin/README.md) and
[generated C++ group reference](generated/cpp-config-reference.md) cover the concrete DTOs.
An unsupported variadic signature needs an explicit fixed native wrapper with correct C
argument promotions. A nontrivial C++ record must not be mapped to a same-sized managed
struct merely to make generation compile.

## Static managed projection inputs

`api.projectionInputs.sources`, `references` and `defines` supply static analysis inputs for projection, compile-time constants and external managed types. They are not project build instructions. Compile handwritten partials and external types in your consumer. Compile, link and deploy native libraries and generated bridges with your external build system.

## Runtime package or source delivery

`runtime.mode = package` references the independent `BGCS.Runtime` consumer library.
It has no generator or parser dependency. `source` emits the selected source closure under
`runtime.namespace`, or `<api.namespace>.Runtime` by default. Namespace changes are applied
structurally. Single-file layout can include that closure in `Bindings.cs`.

The package and source variants must pass the same codec, module, handle and callback tests.
Do not add duplicate Runtime implementations for one binding. Multiple source-runtime bindings
use separate API-local namespaces; a custom namespace is an explicit collision responsibility.

## Local composition, profiles and path ownership

`extends` lists local configuration paths composed in declaration order. Paths resolve from the document which declares them; URLs are not loaded. Named profiles apply after local composition. Cycles, unknown properties and conflicts must be repaired explicitly. `GenerationRequest.FromFileAsync(path, cancellationToken: token)` supports cancellation of local reads.

## Plugin bootstrap and cache policy

`extensions.plugins` lists strict plugin manifests. Each manifest identifies its exact public
entry type, compatibility major, services and complete executable asset hashes. Bootstrap
loads descriptors/schema before validating plugin settings and custom target choices. Session
factories register neutral services, then the registry freezes. Duplicates, exclusive matches
and conflicting output paths fail deterministically.

Ordinary plugins use `BGCS.Contracts`; a Clang-specific fact contributor is an explicit
worker-only advanced extension. Host and worker resource ownership stay separate. A normal
plugin process/load boundary is not a security sandbox.

Artifact-affecting extension state requires `ICacheFingerprintProvider`, including external
input content/existence facts. Missing fingerprints disable affected cache reuse. Priority,
registration order, configuration, code, headers/search membership, target, toolchain, Runtime
mode and processors contribute to generation identity. Read the report for actual hit/miss/
not-used status: enabling policy alone does not prove a hit.

The reusable stages are the original built-in frontend result and built-in C# emission.
Frontend reuse additionally requires its verified worker/resource closure and pinned .NET
execution environment, effective arguments, source content and include-search directory
membership. Ambient or compiler input effects that cannot be closed disable frontend reuse.
Generated source contributors run before that lookup and their actual captured bytes affect
identity. Projection, source processing and final source-contract validations still execute. Unsupported frontends/backends, incomplete
upstream fingerprints or processors without complete fingerprints disable that stage rather
than reuse uncertain bytes. Fingerprinted processors activate and declare their inputs before
lookup, then run on both cache misses and hits. Entries verify their complete named content set,
and corrupt entries become misses.
`cache.maximumBytes` defaults to 512 MiB and `cache.maximumEntryBytes` to 64 MiB; values
must be positive, an entry is at most 256 MiB, and the total must be at least one entry.
A cache hit cannot skip this request's required build or invocation validation.

## Execution, workspaces and publication

`execution` contains generation/session deadlines.

`execution.diagnostics` controls warning promotion at candidate gates:

```json
{
  "execution": {
    "diagnostics": {
      "warningsAsErrors": false,
      "errorCodes": ["BGCS-SAFETY-OWNERSHIP"]
    }
  }
}
```

Warnings remain warnings by default. `errorCodes` matches complete identities without
wildcards; `warningsAsErrors: true` promotes every warning observed by a candidate
gate, including extension validator warnings. Errors cannot be downgraded, and console
log filters do not affect this policy. A returned data pointer without explicit ownership
or lifetime evidence produces `BGCS-SAFETY-OWNERSHIP`; its correct raw ABI remains
available by default. Declare `semantics.ownedResults`, `semantics.borrowedResults` or
neutral plugin contracts before requesting a friendly resource projection. Callable names
do not establish ownership or encoding, and user assertions remain UserAsserted.
Promoted errors prevent replacement of the previous complete publication. Backup cleanup,
private source-store cleanup or cache-write notices after publication remain post-processing diagnostics; they cannot
roll back a completed publication.
Ctrl+C and SDK cancellation reach the Host and its worker/native process tree. Cancellation
or validation failure must not publish a partial successful artifact set.

```json
{
  "schemaVersion": 1,
  "parallelism": 2,
  "projects": [
    { "id": "core", "configuration": "core/bindgen.json" },
    { "id": "addon", "configuration": "addon/bindgen.json", "dependencies": ["core"] }
  ]
}
```

Workspace IDs are unique, dependencies are explicit and cycles fail before execution.
Outputs cannot overlap each other or protected input/cache roots. Generation and build follow
the dependency graph; failed prerequisites block dependent publication. `diff` previews in
private storage and keeps formal output intact. `inspect` exposes detached native, bridge,
interop and managed facts for review.

Output publication uses ownership, hashes, locks and a recovery journal. Do not use an unowned
nonempty directory, a filesystem root or a path that escapes through a symlink. Temporary
validation output is private; the final manifest owns exact artifacts and obsolete generated
files. Handwritten work belongs outside that ownership boundary.

The release inventory, schema documentation, package closure and actual platform receipts must
agree at the release revision. This guide describes supported inputs and conservative boundaries;
implementation/certification status determines whether the complete 3.0 maintenance gate is met.

Console diagnostic presentation can use `--log-level warning` and an independent
`--frontend-log-level error`. Accepted severities are `trace`, `debug`, `information`
(`info`), `warning`, `error`, `critical` and `none`. Failure diagnostics always remain
visible, and presentation never changes success or removes diagnostics from JSON/Host
results. These preferences are command options rather than generation/cache inputs.

## C pointer factories and explicit member methods

Ownership and presentation are independent inputs. For a native
`Widget* widget_create(void)` / `void widget_destroy(Widget*)` pair and
`int widget_get(Widget* self)`, declare:

```json
{
  "semantics": {
    "ownedResults": [{ "function": "widget_create", "releaseFunction": "widget_destroy", "releaseDomain": "widget", "releaseThreadDomain": "any" }],
    "borrowedParameters": [{ "function": "widget_get", "parameter": "self", "lifetime": "call", "retainsPointer": false }]
  },
  "api": {
    "memberFunctions": [{ "function": "widget_get", "nativeType": "Widget", "receiver": "self", "objectName": "WidgetObject", "name": "Get", "releaseDomain": "widget" }]
  }
}
```

This fragment belongs inside the normal grouped configuration. It produces an
owned factory return and both `widget.Get()` and the checked context facade.
`releaseDomain` on a member is optional; when supplied, it rejects objects from
other allocation/release domains even when their native pointer type matches.
The receiver must have the exact selected record-pointer or opaque alias
identity. Unknown retention, an unsupported destructor protocol or missing
ownership evidence does not create a speculative owning wrapper. See
[Runtime lifetimes](runtime-lifetime.md#explicit-c-ownership-and-receiver-presentation).

## Constant and native enum presentation

`api.constants` selects an existing native scalar or string constant by stable
source identity or an unambiguous original name. `api.nativeEnums` selects an
existing native enum and its existing members. These settings can rename,
replace inert documentation and assert managed values; they do not rewrite
native facts, add native enum members or change an enum's native storage width.
`api.enums` remains the separate facility for defining additional managed enums.

```json
{
  "api": {
    "emitLibraryNameConstant": true,
    "constants": [{
      "constant": "NATIVE_VALUE",
      "name": "PresentedValue",
      "managedType": "long",
      "documentation": "An explicitly asserted managed value.",
      "expression": "6L * 7L + 1L"
    }],
    "nativeEnums": [{
      "nativeType": "NativeMode",
      "name": "Mode",
      "documentation": "Public names for the original native enum carrier.",
      "values": [{
        "member": "NATIVE_MODE_FIRST",
        "name": "First",
        "value": { "kind": "integer", "value": "11" }
      }]
    }]
  }
}
```

Use either typed `value` or `expression` for one member. Typed values preserve
exact integer strings and Unicode text. Expressions must be bounded C#
compile-time expressions supported by the framework, generated constant/enum
carriers and declared `api.projectionInputs.sources` / `api.projectionInputs.references`.
Statements, directives, runtime calls, undeclared consumer-only references,
out-of-range enum values and ambiguous
selectors fail before emission. The planner compiles assertions with warnings
as errors, evaluates compiler constant values and emits canonical inert literals;
the final artifact check rejects changed constant types, values and visibility.
The full consumer build still validates its complete references and source set.

Managed compiler inputs are captured before the emission-cache lookup. The host
freezes every declared source and reference, the framework metadata images and
the complete adjacent dependency closure; even an unselected dependency is an
input. Explicit references have precedence over adjacent lookup, which searches
the referring assembly's directory followed by the declared reference directories.
Missing earlier candidates are tracked, so a new shadowing DLL cannot reuse a
result produced from the previous closure. Assembly identities, original bytes
and effective preprocessor symbols participate in the cache identity, and changes
during generation prevent publication. Metadata inspection never loads consumer
assemblies or executes their initializers. The verification receipt records paths
and content digests without embedding PE images.

An unused framework compatibility facade may forward optional types to an
assembly outside the installed framework. A forwarding-only target is optional
only when the facade has no implementation/type dependency on that assembly.
Selecting a missing forwarded type still fails constant validation. Consumer
assembly references retain their complete dependency requirement, including
unselected members and type-forwarding assemblies.

For example, declare a source containing `Vendor.Values.Answer` or its assembly
under `api.projectionInputs`, then use `"expression": "global::Vendor.Values.Answer"`.
`api.projectionInputs.defines` augments static `net10.0` framework symbols;
symbols such as `DEBUG` must be supplied explicitly. Errors in the selected constant, its field,
signature, alias or dependency fail generation. Unresolved bodies of handwritten
partial methods on the generated API/context can be deferred because constant
evaluation cannot execute those bodies. The consumer build must compile all supplied
sources and method bodies; static binding is not full consumer-build certification.

These overrides retain `UserAsserted` provenance separately from the original
compiler facts. For example, presenting native `42` as managed `43` leaves an
actual native function returning `42`. Changing a named enum value changes the
number passed when that managed name is used; choose such assertions deliberately.
The underlying enum carrier and raw import signatures stay fixed.

By default, `NativeLibraryName` is a public `const string` on the instance
context and, for static style, its shared facade. It is readable before loading
a module and is emitted for all three import modes. Set
`api.emitLibraryNameConstant` to `false` to omit it. Its identifier is reserved
only when enabled, and processors cannot substitute its configured value.

For a declarative consumer-owned import constant, set
`api.importLibraryNameConstant` to a qualified field such as
`Vendor.LibraryNames.Native`, and declare its source in `api.projectionInputs.sources`
or its assembly in `api.projectionInputs.references`. DllImport and LibraryImport keep
the fully qualified field reference in their attributes. Generation compiles
the frozen inputs without executing consumer code and requires one `const string`
whose value exactly equals the resolved import name (`__Internal` for the browser
static module). Missing, mutable or mismatched fields fail before publication.
The declaring type must belong to a consumer namespace outside the generated
namespace; artifact processors cannot introduce a shadow type or change the
reference. This choice is independent of the informational `NativeLibraryName`
constant. FunctionTable uses its module provider or `Load` argument and rejects
this static-import-only setting. A changed source or assembly participates in
cache invalidation and input verification.

## Generated type scope and finite input variations

`api.nestGeneratedTypesInApi` defaults to `false`. Enabling it places generated native
storage, inline-array carriers, native and additional managed enums, and named ABI delegates
inside the public API type, for example `WidgetApi.Packet`. Static style uses that same
public scope while retaining `WidgetApiContext` for independent instance lifetimes.
Split and single-file output preserve identical type identities, layouts and raw calling
conventions. Conflicts with API members and implementation types fail before writing files.

For an existing `int add(int left, int right)`, this configuration adds a checked alternate
input while preserving `Add(int, int)`:

```json
{
  "api": {
    "nestGeneratedTypesInApi": true,
    "functions": [{
      "function": "add",
      "inputVariations": [{
        "parameters": [{
          "parameter": "left",
          "projection": {
            "managedType": "long",
            "managedToNativeExpression": "checked((int){value})"
          }
        }]
      }]
    }]
  }
}
```

The added signature is `Add(long, int)`; overflow prevents the native call. An optional
variation `name` selects a different method name. A conversion targets the existing
friendly input type, so an alternate `Vector3` can first become an existing `Vector2`
before entering its original scalar conversion. Forwarders reuse that operation's pinning,
error handling and cleanup, with no additional raw imports. Instance and shared static
facades use the same checked operation.

Value conversions cannot replace `in/ref/out` borrows or hidden parameters. Ambiguous
base projections and duplicate C# signatures fail. Input variations and default-argument
overloads share `api.defaultOverloadLimit`. These expressions are explicit user code,
with final types and references checked by the consumer build; they do not establish
verified native lifetime or ownership semantics.

## Independent family selection and ordered name replacements

`source.selection.categories` applies an independent allowlist to each native family:

```json
{
  "source": {
    "selection": {
      "categories": [
        { "category": "callables", "includeDeclarations": ["read", "write"] },
        { "category": "records", "includeDeclarations": ["Packet"] }
      ]
    }
  },
  "api": {
    "naming": {
      "replacements": [
        { "match": "Http", "replacement": "HTTP", "ignoreCase": true },
        { "match": "Internal", "replacement": "" }
      ]
    }
  }
}
```

Unlisted families remain unrestricted by these independent lists. Global declaration,
header and category filters still apply. Each family occurs once; selectors must match
that family, and ambiguous overloads require an exact signature or stable ID. Positive
family lists restrict ordinary public roots while preserving required native storage
and explicitly required cleanup. Family exclusions are strict: removing required
storage or cleanup fails with its dependency chain. Explicit template or shim requests
can bypass positive filters, but cannot bypass exclusions.

Family names resolve within the declared family. In a common C declaration such as
`typedef struct Item { int value; } Item;`, the tag and typedef do not make a
`records` selector ambiguous. An implicit same-named typedef follows its tag's
family filters. Explicit alias requests and aliases with distinct names keep their
own required-storage contract; selecting an alias cannot bypass an excluded tag.

Name replacements run in array order after category casing and affixes, before
reserved-name protection. Matching is ordinal and case-sensitive unless `ignoreCase`
explicitly selects ordinal case-insensitive matching. Literal replacements do not use
regular expressions or iterate until stable. Exact `api.names` overrides bypass the
policy. Empty results, invalid C# names and signature collisions fail; native export
names and storage remain unchanged.

## Explicit direct string results

Declare encoding and extent independently from the native ownership protocol:

```json
{
  "semantics": {
    "ownedResults": [
      { "function": "get_text", "releaseFunction": "free_text", "releaseDomain": "library-text", "releaseThreadDomain": "any" }
    ],
    "stringResults": [
      { "function": "get_text", "encoding": "utf8", "zeroTerminated": true, "allowsNull": false }
    ]
  }
}
```

This produces a copied managed `string` only when the actual native return is an
integral-code-unit pointer of the declared width and the selected cleanup has the
exact synchronous `void(T*)` signature. The release domain and any-thread property
remain user assertions. Decoding, extent checks and native cleanup run inside one
active module call; `finally` releases every nonnull owned result even when decoding
fails. A null result either returns `string?` with `allowsNull: true`, or fails with
`allowsNull: false`. No encoding is inferred from `char*` or a function name.

UTF-16 and UTF-32 use `encoding: "utf16"` or `"utf32"`; `byteOrder` accepts `native`
(default), `littleEndian` or `bigEndian`. The optional `codeUnitWidth` is checked
against both the encoding and actual native pointee. For borrowed static buffers,
replace the owned rule with `borrowedResults: [{ "function": "get_text", "lifetime": "module" }]`.
That contract must guarantee address validity until the supplying module actually
unloads. A call-bound pointer already expires when the getter returns; an unrelated
owner requires its own protocol and cannot be extended by this copy wrapper.

Choose exactly one extent: `zeroTerminated: true`, nonnegative `fixedLength`, or
`lengthParameter`. Exact lengths exclude a trailing terminator and preserve embedded
nulls. `lengthUnit` is `codeUnits` (default) or `bytes`; byte counts must contain
complete code units. A scalar input count uses `lengthDirection: "input"`. A private
output count uses `lengthDirection: "output"` and the explicit
`lengthOutputCallBounded: true` guarantee that native code writes it synchronously
without retaining its address. The generated signature hides only that private
output count. Negative, overflowing and excessive lengths fail before copying.

`maximumCodeUnits` defaults to 1,048,576, including a terminator during scanning.
It is a scan/copy limit, not a claim that the whole range is allocated or readable.
Zero-terminated native storage must be valid through its first zero code unit.
The scalar scan stops there and strict decoding reads only the discovered extent;
larger valid strings require an explicit higher cap. Unknown ownership, missing
null/extent evidence, incompatible release signatures, status/async results and
unsupported callback combinations retain their raw ABI and an omission diagnostic.

## Documentation on managed-only enums

`api.enums` declares additional managed enums independently of native raw storage.
The enum and each value accept optional plain-text `documentation`:

```json
{
  "api": {
    "enums": [{
      "name": "Status",
      "underlyingType": "uInt32",
      "documentation": "Application status values.",
      "values": [{ "name": "Ready", "value": "1", "documentation": "The operation is ready." }]
    }]
  }
}
```

Prose becomes escaped XML summary text. XML tags, `//` markers and source-looking
lines remain data; documentation does not accept source statements or attribute
strings. Omitted documentation produces no comment unless
`api.emitPlaceholderComments` is enabled. The same rule applies to the enum and
its values, including nested types and split/single-file output. Documentation
does not change the explicitly selected integral carrier, value or Flags contract,
and declaring a managed enum does not automatically replace a native ABI type.

## Explicit members on non-owning addresses

`api.pointerFunctions[].receiver` selects one parameter already mapped in that operation's `parameters`. The generator adds an instance method to the selected address carrier. It requires the API context explicitly: `address.Read(api, "AB")`. Factories remain on the API context. The address stores only its pointer; the method forwards through the supplied context's checked operation, preserving string conversion, `ref`/`out`, temporary cleanup and module call leases. A null or closed context fails before native invocation.

For example, configure `{"function":"read_node","name":"Read","receiver":"self","parameters":[{"parameter":"self","carrier":"NodeAddress"}]}` after selecting `NodeAddress` in `api.pointerCarriers`. The `self` parameter may occur anywhere in the native signature. The original raw signature and context method remain available. A receiver cannot replace owned or module-borrowed wrappers, remove const qualification, or hide required address members. Every pointed allocation must remain valid under the caller's protocol; adding a member neither owns nor retains it.

## Counted string inputs

Explicit input contracts can expose `void set_name(const char* data, size_t length)` as `api.SetName(string)` when encoding, length units, call lifetime and nonretention are declared. Select `zeroTerminated: false`, `lengthParameter: "length"`, `lengthDirection: "input"`, a positive `maximumCodeUnits`, and explicit null allowance. `lengthUnit` is `codeUnits` or `bytes`; UTF-16 code units, UTF-32 scalar code units and UTF-8 bytes are counted after strict encoding, independently of .NET string length. Embedded zero units are preserved. A null string produces a null address and count zero only when allowed; an empty string has a non-null address and count zero.

The generated convenience method hides only the independently selected integral length parameter. Checked conversion rejects lengths that do not fit that native parameter before invocation. Temporary text uses the selected matching allocator and cleanup pair. The exact pointer and length remain available on `Raw`. This projection does not support fixed extents, retained text, output length channels or a length shared with another text/buffer conversion; those require their own explicit protocol. Zero-terminated inputs retain their existing truncation checks and reject embedded nulls.

## Native C++ text value results

Native C++ text values can also be copied into a managed `string` before their creating call ends. This requires a fresh owned holder, exact count and bounded bulk-copy exports, explicit string encoding, and a same-creating-call cleanup capability. Exact standard `basic_string` types with default `char_traits` and `allocator`, and `filesystem::path`, can carry compiler-checked finite cleanup evidence. This permits synchronous destruction on the creating caller thread; it does not authorize escaping handles, arbitrary cleanup threads or finalizer destruction. Custom traits, allocators and custom families do not inherit that capability from a familiar type name or a `noexcept` declaration.

Use exact named aliases such as `std::string`, `std::u16string` and `std::u32string` in `cpp.familyBindings[].nativeType` to attach the corresponding explicit `stringEncoding`. These alias selectors still resolve actual compiler template arguments and reject conflicting bindings. A `char` string does not automatically become UTF-8. Path conversion uses the selected `pathFormat`, including `nativeUtf8` when native separators must be preserved. `api.maximumValueStringCodeUnits` defaults to 1,048,576 and bounds the complete copied extent. Unknown encoding or cleanup facts leave the exact Raw holder operations available with an omission diagnostic.

The text-returning facade retains one resource lease and one call throughout factory invocation, count validation, one bulk copy, strict decoding and matching destruction. Empty text remains `string.Empty`; embedded zero code units are preserved. A count/copy mismatch, invalid Unicode or an excessive extent still runs matching cleanup. An unexpected destruction failure retains the module and faults unload without retrying an ambiguous release; combined copy and cleanup failures remain observable together.


## Optional complete Runtime source delivery

Source mode emits the smallest dependency closure required by the generated API by default. To include the complete public Runtime implementation for handwritten consumers as well, use:

```json
"runtime": {
  "mode": "source",
  "sourceFeatures": ["runtime.complete"]
}
```

This includes the module, handle, callback, async, codec, allocator, Bool8/Bool32, Pointer/ConstPointer, bitfield, special storage and raw interop utilities. It adds source files and no generator or Runtime package reference. Split and single-file output follow the normal output configuration. `sourceFeatures` is an exact, case-sensitive list with no duplicates and is valid only in source mode. The SDK exposes `RuntimeSourceCatalog.GetFeatureIds()` for individually selectable catalog features and `EmissionRequest.additionalRuntimeSourceFeatures` for direct backend use. Configuration, source catalog implementation identity and selected features participate in generation caching.

Including an implementation does not grant an unsupported calling ABI or runtime capability. Special numeric carriers remain storage types; dynamic module loading and dynamic delegates remain subject to the selected execution environment. A complete source consumer and the Runtime package expose the same public Runtime types and members. Each library can use its own source namespace without sharing mutable module state.
