# BGCS.Engine

This internal assembly coordinates configuration, sessions, analysis, bridge
planning, C# generation, caches and transactional source publication. It is
distributed inside the complete `BGCS` product, not as a separately installed
NuGet product.

Engine depends on neutral Core contracts and generation algorithms. It has no
reference to a concrete host, target, frontend or optional platform extension.
An independent composition registers its required implementations through
`BindingHost.CreateBuilder()`; missing or conflicting providers fail explicitly.

For the standard ready-to-use composition, reference `BGCS` version `4.1.2` and
call `BindingGenerator.CreateDefault()` from your own C# generation tool. See
the [C# API](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/api.md) and
[getting started](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/getting-started.md).

The product emits source, manifests and diagnostics. Consumer compilation,
linking, packaging, deployment and execution are external. Running generated
bindings requires only their Runtime support and native module, not Engine.
