# Native shim example

From this directory, after building or installing the `bindgen-cs` tool:

```bash
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
dotnet run --project Consumer/Consumer.csproj
```

The grouped configuration keeps the project-private `Example::Widget` out of the
public entry header. `cpp.nativeShims` declares a self-contained C header, implementation
source and exact exports. The worker parses that public ABI and native construction
includes the declared shim source. The consumer compiles the host target's managed
files and copies its native binary into the executable directory. It prints `42`.
The repository Runtime project reference can become `BGCS.Runtime` version `3.0.0`
for an external consumer after publication.

The default API is an instance context with FunctionTable imports. The consumer
explicitly owns the returned pointer, pins its buffer only for the synchronous call,
checks status, and releases the object before its context closes. These are handwritten
protocol assertions in this sample; a pointer signature alone cannot establish them.
A native exception is caught inside the shim and returned as status.

To generate only the native bridge, use `bindgen-cs bridge bindgen.json`; this omits
managed emission. Its neutral `native/metadata/bridge.manifest.json` can then be handed
to `native-build` with an explicit target manifest path. The normal `build` command
performs managed/native candidate validation and complete joint publication in one request.
