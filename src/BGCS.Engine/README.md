# BGCS

`BGCS` is the complete .NET 10 / C# 14 BindGen-CS generator SDK. Call it as an
ordinary library when an application or build script needs C/C++ binding source.
The CLI product `BindGen-CS` calls the same pipeline. `BindingHost` is a request
coordinator, not a network service.

After the matching version is published:

```bash
dotnet add package BGCS --version 3.0.0
```

```csharp
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"));
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

The package includes analysis, C++ lowering, C# emission, isolated Clang worker
resources and schema support. Internal implementation assemblies are not separate
feature packages to assemble. Restored content supplies the host worker for local
first-use provisioning; no BGCS build targets are required.

Inputs are local configuration, headers, explicit SDK/target settings and plugins.
Outputs are C#, optional C ABI bridge source, manifests and diagnostics. The consumer
compiles, links, packages and deploys them externally. The SDK does not build consumer
projects or native libraries and does not run native layout probes.

Generated applications normally reference only `BGCS.Runtime`, or request an emitted
Runtime source closure. They do not need the generator/parser. Ordinary extension
projects can reference only `BGCS.Core`; parser-specific contributors use the
advanced frontend surface and execute in the worker.

- [Getting started](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/getting-started.md)
- [Public API](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/api.md)
- [Configuration](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/configuration-guide.md)
- [Architecture](https://github.com/FLwolfy/BindGen-CS/blob/main/docs/architecture.md)

BindGen-CS is licensed under MIT. Retained third-party components preserve their notices.
