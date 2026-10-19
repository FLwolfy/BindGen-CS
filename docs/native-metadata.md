# Native name attributes

For generated native files, input roots and consumer source selection, see
[portable native source inputs](native-source-inputs.md). That typed manifest is
independent of the optional C# attributes described here.

Set `api.emitNativeMetadata` to `true` to annotate canonical `RawApi` methods and the corresponding DllImport/LibraryImport declarations. The default is `false`.

The annotations retain the original native callable identity, final C ABI return type spelling, and each original parameter name and type spelling. Managed renaming does not change those facts. FunctionTable uses the same canonical RawApi annotations. Converted friendly methods, aliases, type/field declarations and non-owning pointer views are outside this option's scope.

```csharp
var method = typeof(NativeApi.RawApi).GetMethod("SampleAdd")!;
var names = method.GetCustomAttributes(
    typeof(BGCS.Runtime.NativeNameAttribute), inherit: false);
var nativeReturn = method.ReturnParameter.GetCustomAttributes(
    typeof(BGCS.Runtime.NativeNameAttribute), inherit: false);
```

`NativeNameType.Func`, `Type` and `Param` distinguish the callable, return/type and parameter names. For a C++ bridge, the callable identity can retain its original qualified source name while the type spellings describe the checked final C ABI, including any generated status/result channels. These attributes do not certify calling compatibility or ownership.

The backend plans metadata from immutable interop facts and emits typed syntax with escaped string literals. Final artifact validation checks the exact attribute identity, argument values, target, cardinality and position before removing only those verified annotations for the independent ABI checks. Processors cannot change the metadata or hide other changed attributes behind it.

Package Runtime uses `BGCS.Runtime.NativeNameAttribute`. Source Runtime includes the closed `metadata.native-names` feature under the selected Runtime namespace, including single-file output. Disabling the option omits the annotations and that source feature. Artifact and ABI JSON metadata remain available independently.

The focused regression sources are `tests/BGCS.CSharp.Tests/NativeMetadataTests.cs` and `tests/BGCS.Tests/NativeMetadataTests.cs`. Platform certification still requires executed same-revision receipts.

## External compilation

BGCS generates C# bindings and, for supported C++ declarations, C bridge sources.
Your native build system owns compilation, linking, export checks, packaging and
deployment. Your managed consumer owns C# compilation. A successful generation
result proves none of those later steps or actual invocation.

Run `bindgen-cs generate bindgen.json` (or `bridge` for bridge-only output).
Use `GenerationResult.nativeSources` or `BindingArtifactManifest.ReadNativeSources`
to select the exact target's generated translation units, shim sources and ordered
include requirements. Remap the logical input roots as described in the
[source-input guide](native-source-inputs.md). Add the selected files and upstream implementation to your
CMake, Meson, MSBuild or compiler build. Preserve target architecture, calling
conventions, packing, language mode, defines, includes, SDK and C++ ABI settings.
Link the upstream library and its dependencies explicitly. Generated headers do
not supply missing implementations.

For example, the QuickStart fixture has an ordinary native project:

```bash
cmake -S examples/QuickStart -B artifacts/quickstart-native
cmake --build artifacts/quickstart-native --config Release
bindgen-cs generate examples/QuickStart/bindgen.json
```

Compile the generated `managed` files in a consumer project with unsafe enabled
and `BGCS.Runtime` (or the emitted source Runtime). Supply your built library to
`NativeApi.Load(absoluteLibraryPath)`, or link it into an application using the
consumer platform's ordinary native items. Static browser/NativeAOT/Apple inputs
remain the application's explicit linking responsibility.

`library.nativeName` is preserved for every import strategy, including static
adapters and their export-table imports. It must match the module registered by
the consumer. In .NET 10 browser projects, `NativeFileReference` filenames supply
the P/Invoke module names; for example, `api.c` or `api.a` registers `api`.
Configure `nativeName` as `api` for that module. Select `__Internal` explicitly
only when the consumer registers that name. A static adapter selects the calling
and lifetime strategy; it does not register or rename a native module.

The repository's `scripts/native_fixture_build.py` exists only for acceptance
fixtures. It is not shipped in the SDK or CLI and is not a native provider API.
The real consumer gates build native code externally, compile generated C# and
invoke the result. A parse/layout check cannot stand in for that evidence.
