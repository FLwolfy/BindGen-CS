# Small C API

This example generates a C# binding for `bgcs_add` through the same CLI used by consumers.
Run the commands from this directory with the repository's .NET SDK available:

```sh
dotnet run --project ../../src/BGCS.Tool -c Release -- generate bindgen.json
dotnet run --project ../../src/BGCS.Tool -c Release -- build bindgen.json
```

The grouped configuration selects the host target, an instance API and FunctionTable imports.
`generate` produces managed sources and metadata under `Generated/<resolved-target>/`.
`build` also compiles `native.c`, verifies exports and compiles the generated managed
sources before publishing that complete artifact set. A native toolchain is required.
The generated code references the independently packaged `BGCS.Runtime` library.
Choose `standalone.json` when the consumer needs the runtime as generated source:

```sh
dotnet run --project ../../src/BGCS.Tool -c Release -- build standalone.json
```

That configuration inherits the same source and native build inputs through `extends`.
It emits one `managed/Bindings.cs` containing the selected Runtime source closure.
Consumers of this source variant do not add `BGCS.Runtime`.

For the package variant, compile the files in the selected target's `managed` directory,
reference `BGCS.Runtime`, enable unsafe blocks, and use:

```csharp
using QuickStart.Generated;

using NativeApi api = NativeApi.Load("/absolute/path/to/the/built/native/library");
int answer = api.Raw.Add(19, 23);
```

The native filename is `quickstart.dll`, `libquickstart.so`, or `libquickstart.dylib`,
according to the resolved host. The `Raw` entry is always available for this exact scalar
signature. See [independent invocation tests](../../docs/testing.md) for the broader
ABI and lifecycle matrix. A successful generation or compilation alone is not invocation evidence.
