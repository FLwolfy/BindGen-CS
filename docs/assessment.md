# BGCS 3.0 engineering assessment

[简体中文](assessment.cn.md) | [Capabilities](capabilities.md) | [Acceptance](acceptance.md)

Assessment date: 2026-10-18. This assesses the current generator-only implementation
and its source boundaries. It does not declare all functional acceptance complete,
certify a release revision, or establish that a package has been published.

Core feature baselines are mandatory acceptance. Source/API/architecture checks,
local executable fixtures and same-revision acceptance receipts establish different
facts and must be reported separately. AOT/Wasm application compilation and execution
are optional private consumer checks, not mandatory product acceptance. Target facts
and source generation for those profiles remain part of the generator's scope.

## Seven dimensions

| Dimension | Current assessment | Material limits |
| --- | --- | --- |
| Functionality | C/C++ source generation, three import styles, package/source Runtime, instance/static presentation, plugins, cache and workspaces have implementation paths and focused fixtures. | Raw ABI, native holders and friendly managed projections have different scopes. Source presence does not close each baseline or establish release certification. |
| Code quality | Immutable models, finite typed operations, explicit refusals and final generated-contract inspection support reviewability. | ABI and lifecycle checks require substantial planner/verifier/cleanup code. Formatting and passing tests do not prove ideal simplicity or eliminate maintenance risk. |
| Architectural clarity | Eleven product projects have declared dependency boundaries; neutral analysis and managed emission do not retain Clang ASTs. | Host orchestration remains substantial. New semantics should stay in their owning layer, and public helper growth needs review. |
| Extensibility | Contracts-only factories cover inputs, target descriptions, transformations, lowerings, projection, emission, processors and validators. State and external inputs participate in cache eligibility. | Plugins must supply complete identity, evidence and lifetime contracts. Advanced Clang contributors have a deliberately different dependency and compatibility surface. |
| Usability | Grouped configuration and init/doctor/validate/generate/diff provide one generation workflow. Defaults are instance APIs, FunctionTable imports and split files; consumers integrate generation explicitly. | Headers and target SDK inputs must be configured. Encoding, allocator, concrete templates and lifecycle semantics may need explicit declarations. Consumers own compilation, linking and deployment. |
| Decoupling | Native facts, bridge operations, interop ABI and managed presentation are separate models. The worker isolates parsing; Runtime has no generator dependency. | Replacing a frontend requires equivalent facts. A new target description alone does not establish ABI support. |
| Cross-platform design | Host parsing, target identity/SDK/ABI and runtime/import capabilities are separate. GNU and musl remain distinct target facts. | Target modeling and generated code are different from an application verified on that platform. Unsupported profiles retain explicit refusals; owning virtual registrations remain omitted on nonblocking browser profiles. |

## Product responsibilities

| Project | Responsibility |
| --- | --- |
| BGCS.Contracts | Dependency-free detached models and ordinary extension contracts. |
| BGCS.Infrastructure | Input snapshots, cache/publication, plugin loading and worker transport. |
| BGCS.Frontend.Clang | Parse C/C++ with the configured target inputs and detach compiler facts. |
| BGCS.Frontend.Clang.Worker | Run that parser and advanced contributors in an isolated process. |
| BGCS.Platforms | Resolve target, ABI and read-only toolchain/SDK descriptions. |
| BGCS.Analysis | Selection/closure, layout and semantic/safety validation. |
| BGCS.Cpp2C | Plan C++ lowerings and emit C ABI bridge sources and native assertions. |
| BGCS.CSharp | Plan managed presentation, emit C# and inspect the emitted contract. |
| BGCS.Runtime | Binding call, memory and lifecycle primitives, independent of the generator. |
| BGCS | Compose generation, extension sessions, input verification and publication. |
| BGCS.Tool | Expose the generator through its CLI. |

BGCS emits source; an external build system compiles and links it. The product does
not run a native build, substitute a selected compiler's preprocessed declarations,
or inspect built native objects as a generation step. Emitted native assertions are
source for the consumer's compiler, not proof that compilation has already passed.
Managed projection inputs are captured source/metadata/defines for static Roslyn
binding. They do not launch a consumer project, emit/load its assembly or execute a
layout probe. Scalar field-copy projections preserve generated raw ABI carriers;
unknown managed layout cannot justify direct ABI reuse.

Contracts and Runtime reference no other BGCS project. Ordinary lowerings use neutral
contracts. Frozen registrations, session disposal, input snapshots and independent
output checks enforce the boundaries. The registered Frontend.Tests friend is a test
exception; production friends and shared production compilation links remain forbidden.

## How to extend

An ordinary extension references `BGCS.Contracts` and registers session-owned factories
through `IBindingPlugin`. Use `ICppTypeLowering` or `ICppCallableLowering` for a typed C++
protocol, `IManagedApiTransform` for presentation, `ISourceContributor` for source inputs,
`INativeTargetProvider` for target facts, or `IBindingBackend` for source emission.
Recipes and explicit native shims can address finite library-specific conversions.

Plugin manifests declare compatibility, service identities/priorities, settings and
locked inputs. `ICacheFingerprintProvider` describes state and external dependencies;
incomplete fingerprints prevent the affected cache reuse. CLI, SDK and workspace
requests feed the same generation pipeline. See [extensibility](extensibility.md) and
[the cookbook](cpp-extension-cookbook.md).

`IClangFactContributor` is the advanced alternative for parser-specific facts. It runs
inside the worker against the exact parser API. Borrowed parser handles cannot escape
the contribution call; ordinary Contracts-only plugins do not need this dependency.

## Can most libraries be bound easily?

A public fixed-prototype C ABI with representable layouts is usually the most direct
case. Friendly string/Span/owned APIs still require semantics absent from headers.
C++ objects, inheritance, concrete template instances and finite standard/custom
family protocols have paths, but arbitrary templates or collection results are not
automatically natural C# APIs. Variadic support requires explicit finite wrappers;
chrono values retain actual native units/clock facts rather than an invented epoch.
Unknown ownership, cleanup or ABI classification causes refusal, a reported friendly
omission, or a request for an explicit shim.

There is no evidence-based percentage promise that most arbitrary C++ libraries are
easy. Complex allocators, callbacks, cancellation, custom containers and compiler-specific
header branches need project work. Clang's parsed facts do not establish equivalence
with every downstream compiler configuration. Repository fixtures can compile, link
and invoke generated bindings as core-feature evidence; that work is external to the
product. Such finite checks do not certify an entire GUI, audio-device or GPU system.

The remaining completion question is evidence against the current core baselines,
followed separately by the applicable distribution/release procedure. Optional private
AOT/Wasm application checks must be labeled with their actual host, revision and scope;
neither their presence nor their absence changes a source-generation claim into
universal platform certification.
