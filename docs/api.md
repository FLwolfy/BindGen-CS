# C# generation API

Reference `BGCS` for standard generation. `BGCS.Core` contains data and interfaces; it does not run a generation session. All products require .NET 10 / C# 14.

```csharp
using System;
using BGCS;

await using BindingHost generator = BindingGenerator.CreateDefault();
GenerationRequest request = GenerationRequest.FromFile("bindgen.json");
GenerationResult result = await generator.GenerateAsync(request);
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

`FromFile` creates a descriptor without reading configuration. The selected host materializes configuration, local extends and manifests at session startup. Cancellation passed to `GenerateAsync` also covers this loading. Programmatic requests use `new GenerationRequest(configuration, absoluteBaseDirectory)` and freeze settings defensively.

## Composition

`BindingGenerator.CreateBuilder()` starts with standard implementations. Register interfaces with `RegisterFactory<T>(descriptor, context => implementation)`. To replace a directly registered implementation, call `ReplaceFactory<T>(existingId, descriptor, factory)` before building. A different port, duplicate identity or missing replacement target fails.

`BindingHost.CreateBuilder()` belongs to the neutral Engine and starts without platform implementations. Advanced composition must supply host, frontend, native target, managed runtime and call ABI providers. It does not silently restore defaults.

A grouped `IBindingPlugin` can be added with `AddPlugin<T>()`, `AddPluginFactory(...)` or an explicitly deployed local manifest. A group cannot change the host rules after configuration loading. Per-session factories must not return shared mutable instances.

## Operations

| Operation | Result |
| --- | --- |
| `AnalyzeAsync(AnalysisRequest, token)` | Detached native facts, dependencies and diagnostics. |
| `GenerateAsync(GenerationRequest, token)` | Managed source, required native source, manifests and diagnostics. |
| `BridgeAsync(GenerationRequest, token)` | Verified C/C++ bridge source and source requirements. |
| `InspectAsync(GenerationRequest, token)` | Generation model inspection through the same flow. |
| `DiffAsync(GenerationRequest, token)` | Candidate comparison without replacing formal output. |
| `ClearOwnedOutputAsync(GenerationRequest, token)` | Remove only verified output owned by this library. |

No operation compiles or runs a consumer. `BindingHostBuilder.Build()` constructs the coordinator; it is unrelated to compiling a project.

## Results and commit state

`GenerationResult` exposes `artifacts`, `nativeSources`, session-local `nativeInputRoots`, `targetModels`, `capabilities`, `cacheStages` and `diagnostics`. `success` requires complete publication without an error. `published` and `publicationState` distinguish a pre-commit failure from a valid commit followed by infrastructure cleanup failure.

The native source manifest contains logical roots and physical export mappings. Machine-specific root bindings stay separate and are not serialized into the portable manifest. A consumer maps these IDs to its frozen inputs. See [source requirements](native-source-inputs.md).

Each cache receipt names the stage reused. It is not evidence that the whole request or any native call has already been validated. Diagnostics carry severity, code, location and bounded details where available.

`DisposeAsync` stops new requests, cancels active work and waits for session release. Generated API contexts own a separate Runtime lifetime; disposing the generator does not dispose application modules.
