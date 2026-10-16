# External native slots with a Runtime-only adapter

The producer exposes a seven-slot table: `table_add` is slot 6,
`table_multiply` is slot 2 and `table_hold` is slot 4. Slots 0, 1, 3 and 5
are reserved and may be null. The binding functions are not DLL exports;
only the bootstrap protocol is exported.

Run from this directory with .NET 10, the BGCS tool, CMake and a native C
toolchain installed:

```sh
bindgen-cs generate bindgen.json
cmake -S . -B artifacts/native
cmake --build artifacts/native --config Release
dotnet run --project Consumer -- /absolute/path/to/custom_table.dll
```

Pass the actual output filename: Windows commonly writes
`artifacts/native/Release/custom_table.dll`, Linux
`artifacts/native/libcustom_table.so`, and macOS
`artifacts/native/libcustom_table.dylib`.

`Consumer` references only `BGCS.Runtime`. It compiles the generated files and
the small `CustomSlotTable` adapter. The adapter validates the producer's
identity, original table extent, pointer width and callable slot width. It
selects the three named addresses and supplies `ExternalFunctionTableProvider`
to `CustomTableApi.FromProvider`. The generated code has no knowledge of the
producer's slot positions.

The provider copies the checked names and addresses, requires every supplied
callable to be nonzero, and takes ownership of the module reference only after
construction succeeds. The generated context also resolves its entire required
symbol set before exposing an API. A failed adapter closes the reference it
opened. After a successful transfer, dispose the context; do not independently
dispose its provider while calls or resources remain active. Call leases retain
the module reference until a native call returns, including reverse callbacks.

The identity is an explicit agreement about signatures and layout. Its equality
alone does not prove ABI correctness; the producer must implement `native.h`
with a matching target and calling convention. This example describes a C ABI
with 32-bit `int` and pointer-sized function slots. Targets with a different
slot width are rejected. Equal widths alone do not establish a compatible
function-pointer representation; the adapter's contract additionally requires
the target runtime to interpret each slot as a directly callable address.
The repository's actual generated
consumer test independently compiles this producer, calls the unexported
functions through the custom slots, checks failures and copied table behavior,
and closes a context while a real native call is blocked in a callback.

`BGCS_CUSTOM_TABLE_TEST_FAULTS` enables bootstrap fault injection for that test.
It is not enabled by the example's CMake project.
