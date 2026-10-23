# Capabilities and evidence

Parsing, source generation, consumer compilation and actual invocation are different states. A4.0 capability receipt must identify its source and actual packages; historical3.x results do not automatically certify this implementation.

## Standard composition

The standard product supplies supported desktop host parser resources, desktop header environment resolution, a CoreCLR profile and implemented call ABI rules. GNU and musl remain separate. Unknown libc, missing parser resources or an incomplete explicit target SDK fails.

| Optional implementation | Scope |
| --- | --- |
| BrowserWasm | Emscripten target, Mono Wasm profile, static tables and callback strategy; actual Mono/AOT aggregate execution belongs to its repository gate. |
| NativeAot | NativeAOT profile and existing call mechanisms, without publishing an application. |
| Android | NDK and target inputs; a target name does not select a C# runtime. |
| AppleMobile | Distinct device/simulator and SDK inputs. |
| FreeBsd | Target/sysroot inputs, without automatic CoreCLR or invocation certification. |

Available resources, target resolution and generated source do not establish full platform certification. Unexecuted ARM64 and other combinations stay unverified. Private SDKs use private providers; new ABIs or execution mechanisms need applicable implementations.

## Per-declaration reports

GenerationResult.capabilities and metadata/capability.report.json record exclusions, native bridge/raw/friendly generation and unsupported reasons, with runtime/profile digest and ABI provider. Source manifests separately map logical calls to physical module exports.

Evidence levels are Unknown, Inferred, Declared, UserAsserted and Verified. Provider declarations do not become Verified; production generation does not execute native probes.

Unknown ownership or encoding cannot create a safe friendly wrapper. A correct raw boundary remains available with explicit protocol obligations. Unknown by-value ABI cannot be approved by equal sizeof; use an applicable pointer/shim mechanism or refuse it.

Executed scope is established by exact-commit CI, package and public-consumption receipts linked from [release history](releases/README.md). The [historical ledger](history/4.0-development-ledger.md) describes intermediate attempts, not current platform status.
