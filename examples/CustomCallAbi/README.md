# Custom calling policy

This example supplies `IManagedRuntimeProvider` and `ICallAbiProvider` through a normal plugin. The selected policy accepts only forward `int32` signatures and an external function table. It reuses the public .NET ABI implementation; it does not invent support from matching sizes or claim a new platform.

Build the plugin, reference its project from your generation tool, then register it:

```csharp
using BGCS;
using BGCS.Examples.CustomCallAbi;

await using BindingHost host = BindingGenerator.CreateBuilder()
    .AddPlugin<IntegerOnlyPlugin>()
    .Build();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"), cancellationToken);
if (!result.success) throw new InvalidOperationException(result.GetDiagnosticSummary());
```

Use this directory's configuration. The runtime identity must be selected explicitly. Missing providers and unsupported signatures fail; another provider cannot silently substitute a desktop profile. At consumption time, supply the exact named `Add` function through `FromProvider` and its module-lifetime protocol. The consumer owns compilation, symbol acquisition and deployment.

The plugin references the neutral Core contracts and the public .NET interop extension. The latter supplies the actual scalar and aggregate classification algorithms; changing those algorithms remains an implementation and verification task. The fingerprint includes both assemblies' identities. No compilation or executable probing occurs during generation.
