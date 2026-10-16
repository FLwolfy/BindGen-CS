# BGCS.Cpp2C

`BGCS.Cpp2C` is the C++ bridge implementation inside the complete `BGCS` SDK and `BindGen-CS` tool. Its neutral planner consumes an immutable `NativeModule`, creates typed operations, and emits a C header and C++ implementation. The Host reparses that header in the isolated worker, verifies the actual exports, and restores the bridge's ownership and error contracts before C# generation.

Consumers install `BGCS` for the complete SDK or `BindGen-CS` for the command-line tool. This implementation project is not a separate product package. Standard lowering plugins reference `BGCS.Contracts`; they do not need Clang AST types.

## Generate and build

The same grouped configuration serves C and C++:

```json
{
  "schemaVersion": 1,
  "library": { "id": "widget", "nativeName": "widget_bridge" },
  "source": {
    "language": "cpp",
    "languageStandard": "c++23",
    "headers": ["include/widget.hpp"],
    "includeDirectories": ["include"]
  },
  "api": { "namespace": "Example.Widget", "name": "WidgetApi" },
  "nativeBuild": { "mode": "sources", "sources": ["src/widget.cpp"] },
  "output": { "root": "Generated", "layout": "split" }
}
```

```bash
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

`generate` automatically produces the bridge and managed bindings. `build` additionally compiles the configured native inputs and validates the managed candidates before publishing the joint result. `bridge` explicitly requests native bridge artifacts without requiring the C# backend.

SDK callers use `BindingHost.GenerateAsync`, `BuildAsync`, or `BridgeAsync`. The parser executes in an independent worker process; this planner does not retain parser handles.

## Native contracts

Exports use Cdecl, a fixed-width status return, an optional original-result output, and an owned error output. Native exceptions are caught inside the bridge. Error-message and error-release exports provide an explicit lifetime protocol. Owned object results have a matching destruction export. Arbitrary borrowed pointer and reference results retain an unknown lifetime unless a typed operation or explicit semantics proves one.

Native types remain detached facts with stable identities. Pointer conversions reconstruct qualified names and preserve nested `const` and `volatile`; they do not assume the bridge implementation is inside the original namespace. Multiple inheritance uses native `static_cast`, with `dynamic_cast` for checked conversions from a polymorphic public base.

Opaque standard-library protocols cover string code units, vectors, spans, arrays, maps, sets, optional values, variants, expected values, filesystem paths, chrono representations, and shared/unique holders. Accessors check ranges and value state through native operations. `vector<bool>` uses normalized element copies. Paths expose UTF-8 bytes from `generic_u8string`; a general `std::string` does not itself establish an encoding contract. A by-value `unique_ptr` input moves its payload while its C holder remains caller-owned and valid for destruction.

These protocols expose raw operations. Managed string, buffer, ownership, and callback convenience APIs require their corresponding semantic contracts. Size equivalence does not establish native by-value calling compatibility.

`cpp.sequenceInputAdapters` can add a finite data-and-length input export while preserving the opaque primary export. Scalar and enum elements of strings, vectors, and arrays are copied; spans borrow only identical native element storage with explicit synchronous nonretention assertions. Native fixed extents and lengths are checked. The default managed adapter name adds `WithSequence`; raw primary exports stay available. Pointer/reference/record results and escaping constructors require a separate protocol.

Natural managed names retain ordinary C++ calls, use `Create<Type>` for construction, and use finite operator names. Overloads are validated by C# parameter signatures before emission. Original `sourceSignature` selectors retain parameter types and member qualifiers, including concrete template owner names; exact adapter exports opt into alternate input semantics.

Implicit default construction has a finite operation rather than an invented source function. The frontend records a nullable compiler-evaluated constructibility fact for accessible complete user records. Only true facts in the selected handle graph produce a default-construction export, with its actual destruction pair and deterministic `<Owner>::<Name>()` selector. Native build independently compiles `new Owner()` and a default-constructibility assertion using the final arguments. This fact does not prove release-thread independence or replace final native compilation.

## Explicit templates, recipes, and shims

`cpp.templateInstantiations` selects concrete class and function templates. Class instances are materialized through the worker; function requests create fixed wrappers that forward according to each declared parameter type. Unbound primary templates are excluded from exported callables.

`cpp.typeLowerings` declares fixed C carriers and native input/output expressions. `cpp.callableLowerings` declares exclusions, export names, or an expression wrapping exactly one native invocation. Both compile into the neutral planning interfaces. Native helper fragments carry exact hashes, declared dependencies, and evidence; user expressions remain user assertions and cannot claim independent verification.

An optional `managedProjection` specifies a managed type, input/output expressions, and a required namespace. The C# backend validates all configured projections, including unmatched rules, and preserves exact raw carriers. Native-size aliases resolve from the parsed target pointer width.

`cpp.nativeShims` declares project-owned public C-linkage headers, implementation sources, and exact exports. Headers pass through the worker, sources through the normal native build provider, and selected symbols are checked against detached declarations. BGCS does not infer undeclared shim lifetimes or allocators.

`cpp.virtualCallbacks` selects an abstract native interface. A second C++ worker parse verifies a finite proxy and factory. Metadata records exact Cdecl callback slots with user data, fixed inputs, status returns, and optional result outputs. Callback failures become caught native exceptions. Raw callback addresses must remain valid until proxy release. An explicit `releaseThreadDomain: "any"` assertion permits the managed registration projection; the caller supplies a stop-and-join operation that prevents new native calls and drains every complete proxy execution before destruction. The assertion remains user evidence. Unknown retention or cleanup domains retain the raw API with an explanation instead of an unsafe owning registration. Unsupported exception or type shapes are rejected.

Standard-library holders also keep their cleanup thread domain unknown unless a separate contract establishes it. An `optional<Object>` or copied object may invoke arbitrary user destructors; wrapping it in an STL holder does not prove that releasing it on another thread is valid.

## Artifacts and validation

Native output contains `include/<prefix>.h`, `src/<prefix>.cpp`, `metadata/bridge.module.json` for the typed plan, and `metadata/bridge.manifest.json` for native build inputs. The Host emits the final ABI manifest and verification receipt. Platform support claims come from recorded capability and release evidence.

Neutral unit tests check operation invariants, recipe metadata, template selection, and callback slot planning. Native fixtures exercise the worker, independent C reparse, managed compilation, linking, and real invocation for standard values, concrete templates, configured conversions, native shims, virtual dispatch, and pointer-adjusting inheritance.
