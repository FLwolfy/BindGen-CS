# Native shim example

The C ABI header does not expose the private C++ `Example::Widget`.
`bridge.nativeShims` declares a C header, implementation source and exact exports.
BGCS parses that ABI and emits C# plus the native source requirements; your own
native project compiles and links the shim and any emitted bridge source.

BGCS parses this C header as C. Its implementation remains C++ and is compiled
by the consumer; the generator does not add another C++ exception wrapper around
an already explicit C ABI.

From this directory, use the installed tool:

```bash
bindgen-cs generate bindgen.json
```

Then compile the native sources listed in
`Generated/<target>/metadata/native-source.manifest.json` using your project's
build. Bind its logical input roots to this example's declared inputs, include
the shim implementation, and produce a module containing the listed exports.
The source manifest contains requirements, not a compiler or linker command.

Pass your resulting module's absolute path to the consumer:

```bash
dotnet run --project Consumer/Consumer.csproj -- /absolute/path/to/your/built/widget_bridge
```

Use the actual platform filename, including `.dll`, `.so` or `.dylib` as needed.
The consumer compiles the generated C# and prints `42`. Its repository Runtime
project reference can be replaced by `BGCS.Runtime` version `4.1.0` in an external
consumer. If generation used another output root, set `BGCSGeneratedRoot` to that
root when compiling the consumer; do not edit the generated files.

The default API is an instance context with FunctionTable imports. The consumer
owns the returned pointer, pins its buffer only for the synchronous call, checks
status, and releases the object before its context closes. These are explicit
protocol assertions in the sample; a pointer signature alone cannot establish
them. A C++ exception is caught inside the shim and returned as status.

To request only native bridge source, use `bindgen-cs bridge bindgen.json`.
This omits managed emission. Both commands stop at source, manifests and
diagnostics. BGCS does not compile, link, copy or run the consumer's native module.
