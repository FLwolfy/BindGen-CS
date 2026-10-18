# BGCS 3.0 engineering assessment

[简体中文](assessment.cn.md) | [Capabilities](capabilities.md) | [Acceptance](acceptance.md)

Assessment date: 2026-10-16. This reviews current source, public interfaces and
identified local checkpoints. It is not completed 3.0.0 release certification.
Historical macOS reports and subjective scores are not carried forward as current
proof. The same-revision launch matrix and clean installation gates remain decisive.

## Seven dimensions

The layers, extension contracts and safety mechanisms have substantive implementation
and regression evidence. They support a broad C ABI and a defined set of C++ protocols;
that does not establish every feature on every platform or close the refactor merely
because the project tree exists.

| Dimension | Current assessment | Material limits |
| --- | --- | --- |
| Functionality | C/C++, three imports, package/source Runtime, instance/static APIs, plugins, cache, workspaces and MSBuild share an implementation. Complex lifetimes have actual fixtures. | Raw ABI, native holders and friendly managed projections have different scopes. Arbitrary templates, collection results and unknown ownership are not automatic. Feature inventory needs current item-level evidence. |
| Code quality | Immutable data, finite typed operations, explicit failure and final generated-contract checks improve reviewability; style/API gates constrain regressions. | ABI/lifecycle correctness creates real planner/verifier/cleanup complexity. Formatting is not proof of elegance; public helper contracts require maintenance. |
| Architectural clarity | Thirteen implementation projects have dependency gates; neutral analysis/C# generation do not retain ASTs, the frontend public surface is small and Host coordinates the workflow. | Keep semantics in their owning modules and review new public implementation helpers. Friends or linked production source cannot replace a proper contract. |
| Extensibility | Contracts-only ports cover source input, targets, transforms, lowerings, projection/backend, processors, validation and construction. Inputs/state participate in caching. | Plugins must meet shape, lifetime, evidence and fingerprint rules. Parser-pinned advanced AST contributors are intentionally a different stability/dependency surface. |
| Usability | Grouped configuration and init/doctor/validate/generate/build serve ordinary users; C++ bridge orchestration is automatic. Defaults are instance/FunctionTable/split; MSBuild is opt-in. | Native SDK/compiler setup and project-specific encoding, allocator, template, lifecycle and build facts remain necessary. Not every library is a one-header setup. |
| Decoupling | Native facts, bridge operations, interop ABI and managed presentation have distinct models; a worker isolates parser crashes; Runtime carries no generator. | A replacement frontend must supply equally complete facts. A new ABI/provider still needs actual proof; interfaces cannot erase platform differences. |
| Cross-platform design | Host, target, SDK/toolchain, ABI, runtime and import are separated; desktop/AOT/browser mechanisms differ and GNU/musl assets stay distinct. | Certification requires the same-revision platform matrix. Owning C++ virtual registrations remain omitted on nonblocking browser profiles; Windows/Chromium evidence does not certify other hosts/browsers. |

An average score cannot offset a failed release gate. Record source presence, local
proof and release certification separately.

## Extending the product

An ordinary extension references `BGCS.Contracts` and registers typed ports through
`IBindingPlugin`. Use `ICppTypeLowering` for type semantics, `IManagedApiTransform` for
managed presentation, `ISourceContributor` for generated inputs, `INativeTargetProvider`
for a target. Native construction belongs to external build systems. A finite recipe or native shim
may be enough for a project-specific C++ conversion.

A manifest declares compatibility, IDs/priorities, settings schema and locked assets.
Factories create session-owned services. `ICacheFingerprintProvider` declares state and
external inputs; incomplete fingerprints disable affected reuse. CLI, SDK, workspace
and MSBuild read the same configuration. See [extensibility](extensibility.md) and
[the cookbook](cpp-extension-cookbook.md).

Use `IClangFactContributor` only for parser-specific inspection. It uses the full SDK's
exact advanced parser interface inside the worker; borrowed native handles cannot
outlive contribution. Ordinary plugins do not need those dependencies.

## How complete is cross-platform support?

This is more than a collection of OS flags. The parser follows the host, headers/layout
follow the target SDK/ABI, native construction uses the selected compiler, and managed
emission follows runtime/import capabilities. Actual MSVC/GNU preprocessing and native
proof check that construction has not changed declarations, layout or constants.

That supplies real infrastructure; full platform support still needs actual invocation
and package consumption. Unsupported combinations fail or require a shim rather than
pretending that every ABI is universal.

## How decoupling works

Frontend creates detached native facts; Analysis evaluates selection, layout, semantics
and safety; Cpp2C produces a checked C ABI; CSharp emits from interop/managed models;
External tools own native builds; Runtime handles calls/lifetimes. Host composes
these modules. CLI/MSBuild enter the same pipeline.

Contracts and Runtime reference no other BGCS project. Ordinary lowering does not
reference Clang; parsing lives in a worker. Frozen inputs/registrations and independent
contract checks enforce the boundaries. The sole registered Frontend.Tests friend is
an implementation test exception; production friends remain forbidden.

## Can most libraries be bound easily?

A public fixed-prototype C API with representable layouts is usually the simplest case.
Raw imports often need little configuration. Friendly string/Span/owned APIs still need
semantics absent from headers.

For C++, producing a C ABI bridge and automatically designing a natural C# API are
different tasks. Common objects, inheritance, concrete templates and supported standard
protocols have paths. Complex templates, allocators, retained callbacks, cancellation,
custom containers and unclear lifetimes often need configuration, lowering or a shim.
There is no defensible percentage promise that most arbitrary C++ libraries are easy.
Finite CPU fixtures also do not certify complete GUI, audio-device or GPU rendering
systems.

Delivery still requires generated-source compilation with warnings as errors, actual
native linking/calls and clean installed consumption, with accurate feature scope and
all required evidence bound to the same revision.
