# Extension interfaces

An extension is first an interface implementation in your own C# project. Reference BGCS.Core for generation contracts. Reference BGCS.Runtime for module providers, allocators and release/callback dispatchers used by the running application. A runtime-only consumer need not reference the generator.

## Direct registration

```csharp
using BGCS;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;

await using BindingHost generator = BindingGenerator.CreateBuilder()
    .RegisterFactory<INativeModuleTransform>(new("sample.rules", ExtensionPort.NativeModuleTransform), _ => new SampleRules())
    .Build();
```

The complete [inline example](../examples/InlineExtension/README.md) implements SampleRules. No DLL publishing, manifest, assembly scanning, internal base class or Clang AST is required.

| Interface | Responsibility |
| --- | --- |
| IHostServicesProvider | Session host facilities; selected through code before loading configuration. |
| IFrontend / IIsolatedFrontend | Detached native facts and preparation/dependency inputs. |
| INativeTargetProvider | Target facts and SDK/header inputs. |
| IManagedRuntimeProvider | Open runtime profile and execution constraints. |
| ICallAbiProvider | Exact forward/reverse physical signature classification. |
| INativeModuleTransform | Immutable native fact transformation. |
| INativeTypeLowering / INativeCallableLowering | Typed C/C++ carriers, conversions and operations. |
| IInteropTransform | Final ABI semantics, followed by validation. |
| IManagedProjection / IManagedApiTransform | Primary managed projection or composable presentation changes. |
| IBindingBackend | Source generation. |
| ISourceContributor | Declared immutable inputs before the original parse. |
| IArtifactProcessor | Edit candidate artifacts before final static checks. |
| IGenerationValidator | Static candidate validation; no consumer build port. |

## Selection and lifetime

RegisterFactory is the common path for direct implementations, groups and manifest-loaded code. The descriptor is the single source of ID, port and priority. Duplicate IDs fail. ReplaceFactory explicitly replaces a directly registered same-port service before freezing; it does not create a compatibility alias.

The primary host/frontend/backend must be unambiguous. Target, runtime and ABI providers must have one applicable owner after explicit selection. Priority does not silently resolve competing provider claims. Composable transforms run in descending priority and ordinal ID order.

Every session owns its services. Factories cannot return one mutable instance to unrelated sessions. Services and groups release before publication; host path infrastructure remains alive through final input checks and commit. A group cannot replace the host used to read its own manifest.

## Optional grouping and local loading

IBindingPlugin groups declared services. AddPlugin<T> or AddPluginFactory captures a descriptor and creates session instances. Configure registers only the declared ports through the same registry. Local CLI manifests point to an already deployed, authenticated assembly closure; there is no package installer or marketplace.

Use explicit optional official plugins for additional mechanisms. Installing a package does not activate it. A private SDK need not be copied into BGCS: provide its target inputs, runtime/ABI rules when needed, and declared external inputs.

## Complete cache inputs

Implement ICacheFingerprintProvider for all artifact-affecting state, and IExternalInputProvider for additional files/directories/missing inputs. Code, dependencies, settings, order, target and input identities are included. Captured state that cannot be described completely disables affected cache reuse; delegates are not inspected by private closure reflection.

A SourceContributor supplies bytes, language, role and provenance. The Host captures them once before original parsing. Bridge verification does not call the original contributor again. Artifact processors operate later and cannot silently change raw imports, ABI or planned public signatures.

## What an implementation cannot claim

A target name does not create a new calling ABI or a C# runtime. UserAsserted and Declared evidence do not become Verified automatically. Unsupported direct signatures require a valid pointer/shim path or an explicit failure. Extensions are trusted user code, not a sandbox.

See [architecture](architecture.md), [native source inputs](native-source-inputs.md), [custom target](../examples/CustomTargetPlugin/README.md) and [lowering](lowering.md).
