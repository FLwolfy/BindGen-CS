# Native name attributes

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
