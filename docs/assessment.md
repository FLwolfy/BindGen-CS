# Architecture and product assessment

This assessment separates design properties from executed certification. Current 4.0 execution evidence is indexed under records/acceptance and must match the source/package revision. Historical 3.x receipts are regression references, not a 4.0 pass.

| Dimension | Design and review basis | Practical limit |
| --- | --- | --- |
| Functionality | C/C++ facts, typed native bridges, three imports, raw/friendly APIs, Runtime package/source and 61 retained feature identities. | Arbitrary C++, implicit ownership and unknown ABIs are not supported by guessing. |
| Clarity and maintainability | Descriptors own registration identity; typed calls/conversions carry invariants; verifier checks actual final artifacts. | Coordination and complex C++ protocols still require careful control-flow review; small files alone do not establish quality. |
| Architecture | Core/Runtime independent; Engine separate from default composition; concrete implementations outside neutral modules. | A reproduced model gap may justify a minimal core addition. |
| Extensibility | Inline interfaces, independent target/runtime/ABI providers, lowering, projection and Runtime providers. | A provider name cannot invent a runtime, ABI proof or missing lifecycle protocol. |
| Usability | BGCS is immediately usable with standard implementations; one CLI command and one C# entry. | SDK headers, template selection and library-specific semantics remain real inputs. |
| Decoupling | Native → bridge → interop → managed → emission models; worker isolation; separate generation/runtime dependency closures. | Equivalent frontend facts and explicit physical source requirements are necessary when replacing implementations. |
| Portability | Host resources, target SDK/layout, runtime and call ABI are separate facts; unknown combinations fail. | Resource availability, generation and real execution have distinct recorded scopes. |

## Supported extension paths

SDK paths/macros and existing target rules belong in target providers. Existing-model calling rules belong in ABI providers; execution constraints belong in runtime providers. Library resource protocols belong in semantic configuration/lowering. Native module lookup, paired allocation and release scheduling belong in Runtime interfaces.

Three independent provider fixtures exercise the same Engine without standard implementations. Inline, custom target, custom call ABI and custom lowering examples show code registration without publication. Those test sources become execution evidence only when their exact cases actually run.

## Why Runtime remains separate

Applications need native-call and resource lifetime support, not parser/SDK discovery or generation infrastructure. Module leases prevent unloading code still needed by calls, owned objects, callbacks and operations. Source Runtime is an alternative delivery mode, not a second lifecycle algorithm.

## Limits that remain

A new language mechanism, physical invocation transport or unmodeled lifetime protocol can need a core contract change. New SDK/compiler behavior and ordinary defects can require fixes even without a .NET upgrade. Stable maintenance means changes are evidence-driven and usually localized; it does not mean an immutable core forever.

No consumer project, engine-specific facade, SDK installation, build provider, MSBuild Task or deployment manager belongs to the product. Applicable licenses and honest unverified-platform labels are distribution responsibilities, not legacy compatibility.
