# Native bridges

BGCS plans C and C++ bridges from detached native facts. It writes source and source requirements; the consumer compiles, links and exports the resulting module.

## Calls and storage

The default aggregate strategy is `pointerBridge`: by-value record inputs cross the managed boundary through typed pointers, and record results use typed output storage. Scalars and ordinary pointers do not require a bridge on their own. `direct` requires a complete applicable ABI plan; equal byte size is insufficient.

Inside a bridge, the original language performs the actual native call. C++ objects use constructors/destructors and ownership semantics rather than byte copies. The physical bridge export surface is a single declared module; the source manifest maps each original callable to its physical export. It never guesses a binary filename or link command.

C helper functions, fixed variadic adapters and custom native type/callable lowerings use the same typed plan. Source fragments require explicit input dependencies, digests and contracts. Generated source includes portable logical headers, never an absolute workspace fallback.

## C++

Supported operations include construction, destruction, methods, operators, explicit template instances, native inheritance casts, standard-library value protocols, recipes and shims. Standard string/vector/span/array, map/set, optional/variant/expected, path/chrono and unique/shared pointer protocols have distinct lifetime rules. A `vector<bool>` cannot be treated as a contiguous Boolean array.

C++ exceptions are contained inside the native boundary using the declared status/error/result protocol. The allocator and release function stay explicit; a failed conversion must clean up already-created intermediates.

## Callbacks

Callback signatures have their own reverse call plans. Aggregate inputs and results may require a native adapter, exact user-data association and a normalized result-output argument. Raw callbacks must initialize that result channel on every invocation, including failures. A friendly delegate is omitted with a reason when the configured error contract cannot guarantee that behavior; an arbitrary zero result is not invented.

A call-scoped adapter cannot be retained by native code. A retained context requires an actual returned registration handle and an explicit unregister/join guarantee that no new native calls start and all native frames have exited. Cancellation is not that guarantee. The context, thunk, buffers and module leases survive until the protocol proves quiescence.

## Verification and consumption

The frontend verifies emitted bridge header signatures. The verifier checks names, result/parameter shapes, storage offsets and callback channels and merges original semantics by stable identity. Emitted C/C++ assertions are checked later by the consumer's compiler, not executed by BGCS.

Read [native source requirements](native-source-inputs.md) for logical root remapping, module/export bindings and exact input digests. Consult capability reports for raw/friendly outcomes and remaining consumer responsibilities.
