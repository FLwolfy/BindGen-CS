# Small C API

From this directory, generate with the checked-out CLI:

```sh
dotnet run --project ../../src/BGCS.Tool -c Release -- generate bindgen.json
cmake -S . -B native-build
cmake --build native-build --config Release
```

The CLI generates managed source and metadata under `Generated/<resolved-target>/`.
CMake is the consumer's separate native build; BGCS does not execute it. Compile the
selected target's managed files in a .NET10 consumer with unsafe enabled and a
`BGCS.Runtime` reference. The native filename follows the host build's conventions.

```csharp
using QuickStart.Generated;

using NativeApi api = NativeApi.Load("/absolute/path/to/the/built/native/library");
int answer = api.Raw.Add(19, 23);
```

For emitted Runtime source instead of a package:

```sh
dotnet run --project ../../src/BGCS.Tool -c Release -- generate standalone.json
```

That local configuration inherits the source inputs and emits `managed/Bindings.cs`
with the required Runtime closure. It requires no Runtime package. Consumer compilation,
linking, packaging and deployment remain external for both modes. Generation alone is
not invocation evidence. See [getting started](../../docs/getting-started.md).
