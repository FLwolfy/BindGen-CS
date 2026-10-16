# C++ extension cookbook

[简体中文](cpp-extension-cookbook.cn.md) | [Lowering contracts](lowering.md)

The complete SDK and tool use the same grouped `bindgen.json`. Choose a recipe for a finite conversion, a Contracts-only plugin for a typed planning decision, or a project-owned C shim for native semantics that require handwritten implementation. Each route participates in the Host's worker parsing, ABI verification, caching, candidate validation, and publication.

| Requirement | Extension |
| --- | --- |
| A fixed C carrier and explicit conversion expression | `cpp.typeLowerings` |
| Multiple C inputs representing one native parameter | `abiParameters` and a typed argument group |
| Exclude, rename, or wrap an ordinary native invocation | `cpp.callableLowerings` |
| Match detached declarations and plan additional typed actions | Contracts-only lowering plugin |
| Expose project-private implementation behind exact C exports | `cpp.nativeShims` |
| Materialize a concrete template | `cpp.templateInstantiations` |
| Generate a raw virtual interface adapter | `cpp.virtualCallbacks` |
| Add a checked scalar-element sequence input without removing the raw export | `cpp.sequenceInputAdapters` |
| Reuse a finite container protocol for an exact custom template family | `cpp.familyBindings` |

## Exact custom container families

`cpp.familyBindings` opts a concrete custom template family into one of the finite
`string`, `vector`, `span`, `array`, `optional`, `map`, `set`, `expected`,
`uniquePointer`, `sharedPointer`, `variant`, `path`, `duration` or `timePoint` protocols. Match its exact qualified
template name, without arguments or wildcards:

```json
{
  "cpp": {
    "familyBindings": [
      {
        "nativeTemplate": "Engine::Vector",
        "family": "vector",
        "evidence": {
          "level": "userAsserted",
          "origin": "Engine Vector protocol reviewed against its public implementation"
        }
      },
      {
        "nativeTemplate": "Engine::Text",
        "family": "string",
        "stringEncoding": "utf8",
        "evidence": {
          "level": "userAsserted",
          "origin": "Engine Text stores UTF-8 code units"
        }
      }
    ]
  }
}
```

The native objects retain their actual C++ types in opaque holders. No custom object
is reinterpreted as an STL container. The selected declaration graph must contain a
complete concrete specialization with compiler-provided element and extent facts.
Duplicate exact bindings, contradictory standard-library bindings, a simultaneous
custom recipe for the same type, and missing concrete template facts fail explicitly.

The native compiler checks the particular operations used: exact `value_type` or
`element_type`, unsigned integral counts, element access, assignment, required
`push_back`/`reserve`, optional presence, and borrowed span pointer identity.
Fixed extents are checked at runtime before access or construction. Optional value
access checks presence before calling a potentially unchecked custom `value()`.
Proof-token and native identifier macros cannot silently rewrite these checks.

Map bindings require exact `key_type`/`mapped_type`, matching const `begin()` and
`end()` iterators, boolean equality, increment, exact key/value access and
`insert_or_assign`. Set bindings require exact `value_type`, the corresponding
iterator protocol and `insert`. Both keep index bounds and check the iterator
sentinel before incrementing or dereferencing, so a reported count exceeding the
actual iteration extent reaches the native error boundary. Iteration order remains
the native container's order and concurrent mutation still requires caller
synchronization. No contiguous storage or `Span` lifetime is inferred.

Expected bindings require exact `value_type`/`error_type`, a boolean `has_value()`
and exact const value/error access. The void success-value case has no fabricated
value getter. Access checks the selected state before entering the corresponding
member; failures use the same status/error cleanup protocol as other C++ bridges.
For example, bind `Engine::Result` with `family: "expected"` and the same explicit
`UserAsserted` evidence structure shown above. These protocols do not treat a
lookalike type as an STL implementation or certify its arbitrary behavior.

Unique/shared bindings require exact `element_type` and `get()` pointer types, a
boolean presence member, default construction and nonthrowing destruction. A unique
holder must be noncopyable and have a nonthrowing move; a shared holder must support
nonthrowing copy. Unique value arguments move from their source holder, and shared
value arguments copy the native holder. A returned `get()` address remains borrowed
from its holder. These checks do not prove thread-independent cleanup or an
arbitrary implementation's ownership promises, which remain `UserAsserted`.

A custom variant additionally declares `"variantAccess": "memberGet"`. Its const
`get<I>()` must return the exact selected alternative and `index()` must have a
bounded unsigned integral result. Generated access checks the active index before
calling the member. BGCS does not assume that `std::get` accepts a custom type.

A path binding explicitly declares `"pathFormat": "genericUtf8"`. Its
`generic_u8string()` result needs counted, integral one-byte code units; the encoding
and separator behavior are explicit user assertions. UTF-16 results fail the
one-byte protocol. No platform-native `wchar_t` encoding is guessed.

Duration bindings require concrete representation and positive bounded `std::ratio`
template arguments. Native compilation checks the public `rep`, `period`, normalized
numerator/denominator and exact `count()` result. Time-point bindings require an
established duration family plus exact `clock`, `duration` and `time_since_epoch()`
facts. Counts retain their native representation and rational unit; BGCS does not
silently convert them to nanoseconds or invent a managed-compatible clock epoch.

`variantAccess` and `pathFormat` are rejected on unrelated families. Exact
non-template custom String/Path selectors use `nativeType` and the explicit protocols
below rather than template-head matching.

Those checks establish the finite operation shapes; arbitrary allocation, ownership,
threading, encoding and method behavior remain explicit `UserAsserted` claims. A
family binding does not prove that a returned element borrow outlives its owner or
that destruction is valid on any thread. Custom bool vectors are not classified as
packed storage merely because they select the vector protocol.

Sequence input overloads still require `cpp.sequenceInputAdapters`; borrowed spans
still require explicit call lifetime and nonretention. String bindings require an
explicit UTF encoding matching the compiler's code-unit width. Different member
protocols and additional ownership semantics use a typed recipe,
plugin or native shim. Native build and invocation stages remain pending after
`generate`; inspect the verification receipt before making a support claim.

## Native overload names and selectors

Ordinary C++ calls retain their original method names. Constructors become `Create<Type>`, destructors become `Destroy<Type>`, and finite operators receive names such as `Add` and `ToInt`. Raw and friendly overloads are checked by their complete C# parameter signatures before emission. Return types and nullable-reference annotations cannot distinguish overloads.

An overloaded source selector includes the qualified name, exact source parameter types, and member qualifiers, for example `Demo::Counter::Counter(int)` or `Demo::Counter::Evaluate(double) const`. `inspect` exposes `sourceSignature` so complex template and pointer declarators can be copied. Name mappings prefer exact C export, then exact source signature, then qualified source name. An ambiguous semantic selector fails; specify a source signature.

## Exact string and native path protocols

Use `nativeType` instead of `nativeTemplate` for one actual non-template class or
alias, or an exact named standard `basic_string` alias such as `std::u16string`.
They are mutually exclusive. The canonical class identity remains native;
two configured aliases cannot apply competing protocols to the same class.
Indexed String access requires a public compiler-derived `value_type`, count,
index, append and reserve operations. A smaller native API can explicitly select:

```json
{
  "cpp": {
    "familyBindings": [{
      "nativeType": "Engine::Text",
      "family": "string",
      "stringEncoding": "utf8",
      "stringAccess": "cStrNullTerminated",
      "evidence": { "level": "userAsserted", "origin": "reviewed Engine Text protocol" },
      "cStr": {
        "maximumCodeUnits": 1048576,
        "readableLifetime": {
          "kind": "call",
          "evidence": { "level": "userAsserted", "origin": "readable throughout each access call" }
        },
        "nullTerminated": { "level": "userAsserted", "origin": "terminator within the declared readable bound" },
        "constructorCopiesInput": { "level": "userAsserted", "origin": "Text(const char*) copies before returning" }
      }
    }, {
      "nativeType": "Engine::Path",
      "family": "path",
      "pathFormat": "nativeUtf8",
      "evidence": { "level": "userAsserted", "origin": "u8string returns counted UTF-8 with native separators" }
    }],
    "sequenceInputAdapters": [{
      "function": "Engine::CheckText",
      "parameters": [{ "parameter": "value" }]
    }]
  }
}
```

`cStrNullTerminated` derives its code-unit identity from one accessible exact
zero-argument `c_str()` member. It does not require a `value_type` alias, `size`,
indexing, append or reserve members. Generated Count/GetAt exports use a bounded
scan; null pointers, missing terminators and invalid indices report native errors.
The limit includes the terminator and is not proof that arbitrary storage is
readable. Encoding, readable lifetime and termination remain explicit user claims.
Receiver constness follows the actual getter; no mutable operation is fabricated.

The optional copying-constructor assertion permits an explicitly requested scalar
or void sequence-input adapter. The adapter copies counted units into terminated
temporary storage, rejects embedded zeros and counts at or beyond the scan bound,
and constructs the actual native class within the call. No native pointer may
retain that temporary. Const-reference input also needs the ordinary explicit
call/nonretention assertion. Counted data/length text metadata is preserved for
managed projection; native helper tests alone do not prove a C# string overload.

`nativeUtf8` uses `u8string().data()/size()` and preserves native separators.
`genericUtf8` uses `generic_u8string().data()/size()`. Both require exact unsigned
bounded counts and one-byte integral units under the actual compiler; the returned
counted holder needs neither `value_type` nor indexing. Neither format infers a
custom path constructor or treats a Windows narrow string as UTF-8.

The exact indexed and minimal native fixtures have separate receipts in
`eng/acceptance/cpp-neutral-parity.json`. Their local native stages do not certify
release packages, other operating systems, NativeAOT or browser execution.

## Standard sequence input adapters

For `int Demo::SumVector(std::vector<int> values)` and `int Demo::SumSpan(std::span<int> values)`, add:

```json
{
  "cpp": {
    "sequenceInputAdapters": [
      { "function": "Demo::SumVector", "parameters": [{ "parameter": "values" }] },
      { "function": "Demo::SumSpan", "parameters": [{
        "parameter": "values", "retainsArgument": false, "lifetime": "call"
      }] }
    ]
  }
}
```

The primary opaque export stays available. An additional export carries an element pointer and an unsigned 64-bit element count, with `inputAdapterOf` linking it to that primary. Its default managed name is `SumVectorWithSequence` or `SumSpanWithSequence`; an exact adapter-export name mapping can override it. Source signature and qualified-name semantic selectors continue to select the primary, while exact export selectors can opt into an adapter.

`basic_string`, `vector`, and `array` copy each scalar or enum element into a temporary native value. `vector<bool>` normalizes each element rather than borrowing packed bits. General strings expose code-unit spans and do not infer UTF encoding. Arrays and fixed spans require their compiler-derived extent; null with nonzero length and native `size_t` overflow fail at the native exception boundary.

`span` borrows identical native element storage. A borrowed span or const-reference temporary requires explicit `retainsArgument: false` and `lifetime: "call"`; this remains a user assertion. Writable native spans project to `Span<T>` and const element spans to `ReadOnlySpan<T>`. Distinct bool, enum, and wide-character C++ types cannot borrow normalized carriers. Native compilation independently checks borrowed element type identity.

These adapters support ordinary calls returning scalars or void. Constructors, custom invocation/result expressions, and pointer/reference/record results need an explicit escaping-argument protocol. Mutable or rvalue object references stay raw. Owned-object calls pin spans outside their synchronous handle invocation and capture integral addresses plus checked bounds, so pinning remains active through the call without capturing a ref struct.

## A fixed value recipe

Add this fragment to a complete configuration whose source headers declare `Engine::EntityId`:

```json
{
  "cpp": {
    "typeLowerings": [{
      "id": "engine.entity-id",
      "typePattern": "Engine::EntityId",
      "priority": 100,
      "cAbiType": "uint64_t",
      "parameterExpression": "Engine::EntityId::FromRaw({value})",
      "resultExpression": "({value}).Raw()",
      "managedProjection": {
        "managedType": "EntityId",
        "managedToNativeExpression": "{value}.Value",
        "nativeToManagedExpression": "new EntityId({value})",
        "requiredNamespace": "Engine.Managed"
      }
    }]
  }
}
```

The managed project must supply `Engine.Managed.EntityId` and include its source/reference in candidate validation. Raw imports still use the exact C carrier. BGCS validates managed type and expression syntax even when a recipe's native type pattern does not match.

Input placeholders are `{value}` and `{nativeType}`; output placeholders are `{value}` and `{resultType}`. Native result expressions operate on a local containing the original invocation result, so conversion does not duplicate the native call. Patterns ignore whitespace, use ordinal matching, and accept `*`. Equal-priority exclusive matches fail; identity ordering does not silently choose one.

Native-size aliases `size_t`, `uintptr_t`, `ptrdiff_t`, and `intptr_t` resolve from the actual parsed target pointer width. A C pointer carrier does not establish an allocator, ownership, retention, or encoding contract.

## Multiple ABI inputs

A native `Range` value can be reconstructed from a borrowed data address and length:

```json
{
  "cpp": {
    "typeLowerings": [{
      "id": "engine.range",
      "typePattern": "Engine::Range",
      "cAbiType": "uint64_t",
      "abiParameters": [
        { "name": "data", "cAbiType": "const int32_t*" },
        { "name": "length", "cAbiType": "size_t" }
      ],
      "parameterExpression": "{nativeType}({data}, {length})"
    }]
  }
}
```

`cAbiType` remains the primary representation for native results; `abiParameters` defines the input expansion. Each input name is a unique alphanumeric placeholder beginning with a letter; `nativeType` is reserved. Fixed carriers and one-level pointers are supported.

The planner creates a `BridgeArgumentGroup` keyed to the exact original native parameter. Its bindings refer to the exported C parameters by stable ID. Verification requires the original native parameter order and complete, duplicate-free carrier coverage. An ordinary parameter elsewhere in the same call remains a one-carrier group.

An input expansion requires an explicit fingerprinted native expression. A single-value managed input expression cannot describe pinning or lifetime for multiple carriers; use a typed managed projection with explicit buffer semantics for that convenience API. Raw bindings remain available with the actual expanded C signature.

## Owned recipe results

For an explicitly allocated nonconst pointer result, add `ownedResultRelease` with `nativeFunction`, `allocatorDomain`, and `releaseThreadDomain` to the type recipe. The release name identifies one real native declaration, not a cleanup expression. It must return native `void` and produce a typed bridge export with exactly the compatible C pointer input. The planner brings in that dependency even when ordinary entry selection did not include it.

`releaseThreadDomain` defaults to `unknown`; set `any` only as an explicit assertion of thread-independent cleanup. The resulting `BridgeResultOwnership` stores the actual export ID and allocator domain. Evidence remains `UserAsserted` for configured recipes. Owning wrappers retain a module lease through release; unsupported or mismatched cleanup fails before publication. An owning result cannot also use an unrelated single-value managed result expression.

## Callable recipes

```json
{
  "cpp": {
    "callableLowerings": [
      {
        "id": "engine.checked-add",
        "functionPattern": "Engine::Math::Add",
        "priority": 200,
        "exportName": "engine_math_checked_add",
        "invocationExpression": "Engine::Interop::Check({invocation})"
      },
      {
        "id": "engine.hide-debug",
        "functionPattern": "Engine::*::Debug*",
        "exclude": true
      }
    ]
  }
}
```

The wrapper contains exactly one `{invocation}`. Its headers must already be declared source inputs. An exclusion cannot simultaneously declare helpers, a wrapper, or an export name. Renaming cannot change the actual source parameter/return identities.

## A Contracts-only plugin

The runnable source is [examples/LoweringPlugin](../examples/LoweringPlugin/README.md). Its `BGCS.Contracts.Plugins.IBindingPlugin` registers a session-owned `ICppCallableLowering` factory. The service inspects `NativeDeclaration` and returns a complete `CppCallableLoweringPlan`: receiver representation, exact native member action, C export, and all referenced shapes.

```bash
cd examples/LoweringPlugin
dotnet build BGCS.Example.LoweringPlugin.csproj --configuration Release
python write-manifest.py
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

Regenerate the plugin manifest after rebuilding. It records executable/dependency hashes; plugin and service fingerprints declare private state and external inputs. Unfingerprintable services can run but disable their affected generation cache. Default factories create one service per session. Production plugins do not store Clang handles or mutate the frozen registry.

Native helpers use `ControlledSourceFragment`: exact UTF-8 SHA-256, origin, dependencies, required headers, placement, and evidence. These are explicit source extensions, not a security sandbox. User assertions remain `UserAsserted`; parsing or compiling them does not automatically make lifecycle claims `Verified`.

## A project-owned native shim

The canonical complete example is [examples/NativeShim/bindgen.json](../examples/NativeShim/bindgen.json). It declares:

```json
{
  "cpp": {
    "nativeShims": [{
      "id": "widget",
      "publicHeaders": ["shims/widget_c.h"],
      "sourceFiles": ["shims/widget_c.cpp"],
      "exports": ["bgcs_widget_create", "bgcs_widget_destroy", "bgcs_widget_sum"]
    }]
  },
  "nativeBuild": { "mode": "sources" }
}
```

The public header declares fixed C-linkage prototypes; the implementation includes the upstream C++ header and performs the project-specific conversion. Paths resolve from their declaring configuration. The Host parses the header, checks each exact symbol, and includes sources in its ordinary native build plan.

```bash
bindgen-cs bridge examples/NativeShim/bindgen.json
bindgen-cs build examples/NativeShim/bindgen.json
```

Inspect the generated `native/metadata/bridge.module.json` for the typed operations, `bridge.manifest.json` for build inputs, and the final ABI/verification metadata. Native C symbols alone do not establish correct release affinity or callback quiescence; supply those semantics explicitly before requesting owning or retained convenience wrappers.

## Concrete templates and virtual slots

`cpp.templateInstantiations.types` contains `{ "id": "...", "nativeType": "Engine::Box<int>" }`. Function entries contain `id`, `nativeCallable`, `returnType`, and `parameterTypes`. The worker materializes concrete facts; generated function wrappers use `std::forward` according to the declared parameter types.

`cpp.virtualCallbacks` contains `{ "id": "listener", "nativeInterface": "Engine::Listener" }`. A second ordinary C++ parse validates the generated proxy. Named C callback typedefs are independently reparsed; final `InteropModule.virtualProxies` records exact method names, callback value channels, factory/user-data identities, the real pointer-adjusting interface cast, and paired concrete destruction. The native retention boundary and quiescence remain unknown.

The default `releaseThreadDomain: "unknown"` preserves the raw API. Set `releaseThreadDomain: "any"` only when you can assert that the concrete proxy and its interface base are safe to destroy on any closing thread; this claim remains `UserAsserted`. It enables an eligible caller-controlled managed registration, which still needs a stop-and-join action guaranteeing that **all native proxy execution has ended** before destruction. Preventing new calls and draining only managed thunk entries does not prove that C++ proxy frames have returned.

Keep raw callback addresses and user data valid until proxy release. A nonzero status throws inside the potentially throwing C++ virtual method; BGCS bridge entry points catch that exception. An unrelated upstream thread or call path must provide its own native exception boundary. Nonthrowing or unresolved native exception contracts are rejected by this status-failure proxy strategy.

Profiles with `allowsBlockingDisposal: false` retain the exact raw proxy ABI and
omit this managed registration. Its current shutdown drains complete native proxy
frames synchronously. Retained C callback or async event-loop protocols do not
establish a nonblocking virtual-proxy shutdown protocol; that capability requires
separate implementation and real runtime evidence.
