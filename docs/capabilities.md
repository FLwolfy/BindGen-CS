# BGCS 3.0 capabilities and limits

[简体中文](capabilities.cn.md) | [Assessment](assessment.md) | [Acceptance](acceptance.md)

This describes the current 3.0 implementation. Source implementation, a successful
local test and certification of a released revision are different claims. Neither a
configured CI job nor a historical report certifies the current source.

## Reading evidence

| State | What it establishes |
| --- | --- |
| Implemented source | A model, implementation and validation path exist. |
| Local proof | The named test or fixture executed on its recorded host and compiled bytes. Dirty local checkpoints remain local evidence. |
| Certified revision | All required jobs, actual calls and clean installed-product checks passed for the same release revision and declared platform. |
| Explicit limitation | Missing ABI or lifetime evidence causes rejection, a raw-only API or a reported friendly omission. |

Current local work includes Windows/MSVC generated/native calls, CoreCLR import and
Runtime-mode matrices, NativeAOT, Chromium browser interpretation/AOT, external
plugins and real-library fixtures. Their receipts record different checkpoints and
scopes. They do not collectively certify an untested final revision. Release status
comes from [the required gate manifest](../eng/acceptance/required-release-gates.json),
feature/configuration inventories and same-revision reports.

## C declarations and ABI

| Capability | Implemented behavior and boundary |
| --- | --- |
| Functions, enums, aliases, scalar/string constants and macros | Detached compiler facts preserve identity, signedness, exact values, code units and source. Context-dependent macros are not published as stable probe-site constants. Arbitrary function-like macros need an explicit typed native adapter. |
| Records, unions, arrays, packing and bitfields | Native layout, managed storage and by-value call compatibility are checked separately. Same size alone never proves safe calling ABI. |
| Pointer aliases and opaque records | Raw pointers remain available when representable. Explicit address carriers are non-owning; unsafe dereference views retain caller lifetime responsibility. |
| Strings, buffers and single-value inputs | Friendly string/Span/ref/out projections require encoding, units, bounds, direction and lifetime facts. Unknown pointers do not automatically become strings or spans. |
| Variadic functions | `source.variadicVariants` creates finite fixed native wrappers with native default argument promotions; their fixed ABI can use all three imports. Arbitrary managed vararg calls are not generated. An uninvoked opaque variadic function address is not a callable generic signature. |
| Internal/inline C helpers | `source.includeHeaderHelpers` defaults to false. Opt-in adapters must be compiled into the same configured native module; an existing DLL does not gain their exports automatically. Compiler intrinsics require the bounded provider-specific proof. |
| Special native types | `char`, `long`, `wchar_t`, `char8_t`, `long double` and `va_list` retain target-specific facts. Unsupported by-value forms are refused; pointer or native-shim routes remain explicit. |
| Import modes | FunctionTable, DllImport and LibraryImport consume the same interop model. Their runtime/profile capabilities still apply; selecting an import does not make an unsupported ABI safe. |

## C++ bridge and managed projection

| Capability | Implemented behavior and boundary |
| --- | --- |
| Objects, methods, operators and overloads | Typed bridge operations cover accessible concrete declarations, construction/destruction, status/error boundaries and checked native casts. Deleted, inaccessible or unresolved declarations fail explicitly. |
| Templates and inheritance | Explicit `cpp.templateInstantiations` selects finite class/function instances. Multiple inheritance uses actual native adjustment; a raw reinterpret cast is not substituted. Arbitrary template metaprogramming is not automatically instantiated. |
| Standard and configured families | Strings, vector/span/array, map/set, optional/variant/expected, unique/shared pointers, paths, duration and time points use finite typed protocols. `cpp.familyBindings` matches exact compiler-established types or template families with explicit behavioral assertions. Similar names or sizes do not establish compatibility. |
| Strings and paths | Counted adapters preserve embedded zeros and declared UTF units. Fresh default-standard string/path results can copy to C# string inside a proven synchronous creating-call cleanup scope. Custom allocator/traits/lifetime cases require their own proof or shim. |
| Other container results | Raw/status holder operations and high-level managed collection returns are separate capabilities. Native container support does not promise an automatic C# array or collection result. |
| Chrono | Count, representation, ratio and clock identity are preserved. No universal nanosecond conversion or managed date epoch is inferred. |
| Ownership and exceptions | Owned wrappers need an exact destruction pair and cleanup domain. C++ exceptions are contained at generated C boundaries. Unknown retention or release-thread requirements are not silently invented. |
| Virtual callback proxies | `cpp.virtualCallbacks` provides exact raw proxy operations and, when admitted, typed managed thunks with explicit full native stop/join coordination. Unknown cleanup retains raw only. Nonblocking profiles, including Browser Wasm, currently omit the owning managed virtual registration; ordinary retained C callbacks/async support does not prove this different protocol. |
| Custom lowering | Contracts-only type/callable lowerings, finite recipes, controlled fragments and native shims extend the pipeline. Their assertions and external inputs retain their evidence level and cache identity. |

See [lowering](lowering.md) and [the cookbook](cpp-extension-cookbook.md) for the actual
configuration and prerequisites. A returned `char*` does not establish UTF-8 or ownership;
a method named `Create` does not establish a safe release function.

`AnalyzeAsync` also retains typed Objective-C declaration facts through the worker.
This is [analysis-only support](objective-c-facts.md), not generated message-send,
ARC or Apple runtime binding support; generation rejects that request.

## Runtime, extensions and workflow

Instance contexts, FunctionTable loading, module/call/resource leases, owned and
module-borrowed handles, call-scoped callbacks, explicit retained callback protocols
and finite asynchronous completion projections are implemented. Generated callback
exceptions are contained. Browser nonblocking retention/completion requires its explicit
event-loop protocol; desktop blocking stop/join is not transferred to that runtime.

Standard plugins use only `BGCS.Contracts`: source contribution, target providers,
native/interop/managed transforms, C++ lowerings, managed projection/backend, artifact
processors, validators, native providers and asset packaging have typed ports.
Advanced Clang facts use a parser-pinned, worker-only interface with borrowed handles;
they are a deliberate separate dependency. See [extensibility](extensibility.md).

CLI, embedded SDK, workspaces and opt-in MSBuild use `BindingHost`. Frozen grouped
configuration, deterministic order, dependency snapshots and extension fingerprints
control cache reuse. Requested validation still runs on cache hits. Final contract
checks and owned publication protect the previous complete output on failure.

Defaults are .NET 10/C# 14, C17 or C++23, instance API, FunctionTable, split managed
files, package Runtime and no native construction. The single-file default is
`Bindings.cs`. The same full SDK/tool supplies built-ins; users do not install each
implementation project separately. Exact current fields/defaults come from
[the generated configuration reference](generated/config-reference.md).

## Platform scope

Windows x64, Linux GNU x64, Intel macOS and Browser Wasm are required launch gates;
NativeAOT and installed SDK/tool/Runtime/plugin/MSBuild consumption have separate
requirements. Windows local success does not replace Linux or macOS execution.
A Chromium run does not certify Firefox or Safari. ARM64, musl, Android, iOS and
FreeBSD have their own resolution, generation, compilation, linking, invocation and
package-consumption stages; incomplete stages must remain marked as such.

The architecture separates host parser, native target, SDK/sysroot, compiler, ABI,
managed runtime and import strategy. That is substantive cross-platform support
infrastructure. Full support is established only by completing each platform's actual
matrix, not by listing a target or successfully cross-parsing one header.
