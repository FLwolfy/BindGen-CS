# Tested Configuration Entries

This catalog is generated from `BGCS.Configuration.Tests` fixtures and the current `CsCodeGeneratorConfig` source.
It describes tested behavior; [Configuration Guide](configuration-guide.md) explains composition,
targets, ownership, and failure boundaries. Use the CLI `schema` command for the full binding configuration.

## additionalArguments

- Result property: `additionalArguments`
- Type: `List<string>`
- Source default: `[]`
- Expected fixture value: `["-DFROM_ADDITIONAL_ARGUMENTS=1"]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "additionalArguments": [
    "-DFROM_ADDITIONAL_ARGUMENTS=1"
  ]
}
```

### Generated output assertions

```csharp
// Required markers
EntryPoint = "args_ok"
ArgsOkNative
partial class EntryApi
// Excluded markers
```

## allowedConstants

- Result property: `allowedConstants`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `["KEEP_CONST"]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateMetadata": true,
  "allowedConstants": [
    "KEEP_CONST"
  ]
}
```

### Generated output assertions

```csharp
// Required markers
KEEP_CONST
// Excluded markers
DROP_CONST
```

## allowedDelegates

- Result property: `allowedDelegates`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `["KeepDelegate"]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "allowedDelegates": [
    "KeepDelegate"
  ]
}
```

### Generated output assertions

```csharp
// Required markers
delegate void KeepDelegate(
// Excluded markers
delegate void DropDelegate(
```

## allowedEnums

- Result property: `allowedEnums`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `["KeepEnum"]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "allowedEnums": [
    "KeepEnum"
  ]
}
```

### Generated output assertions

```csharp
// Required markers
enum KeepEnum
// Excluded markers
enum DropEnum
```

## allowedExtensions

- Result property: `allowedExtensions`
- Type: `HashSet<string>`
- Source default: `new()`
- Expected fixture value: `["keep_ext"]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": true,
  "generateRuntimeSource": false,
  "generateFunctions": true,
  "allowedExtensions": [
    "keep_ext"
  ]
}
```

### Generated output assertions

```csharp
// Required markers
partial class Extensions
public static void KeepExt(this
// Excluded markers
public static void DropExt(this
```

## allowedFunctions

- Result property: `allowedFunctions`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `["keep_fn"]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "allowedFunctions": [
    "keep_fn"
  ]
}
```

### Generated output assertions

```csharp
// Required markers
EntryPoint = "keep_fn"
KeepFnNative
// Excluded markers
drop_fn
DropFnNative
```

## allowedTypedefs

- Result property: `allowedTypedefs`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `["KeepHandle"]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "allowedTypedefs": [
    "KeepHandle"
  ]
}
```

### Generated output assertions

```csharp
// Required markers
partial struct KeepHandle
// Excluded markers
partial struct DropHandle
```

## allowedTypes

- Result property: `allowedTypes`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `["KeepType"]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "allowedTypes": [
    "KeepType"
  ]
}
```

### Generated output assertions

```csharp
// Required markers
partial struct KeepType
// Excluded markers
partial struct DropType
```

## apiName

- Result property: `apiName`
- Type: `string`
- Source default: `string.Empty`
- Expected fixture value: `"ExpectedApiName"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "ExpectedApiName",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false
}
```

### Generated output assertions

```csharp
// Required markers
partial class ExpectedApiName
ApiNameFnNative
// Excluded markers
partial class EntryApi
```

## autoSquashTypedef

- Result property: `autoSquashTypedef`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "autoSquashTypedef": false
}
```

### Generated output assertions

```csharp
// Required markers
using BaseInt = int;
using AliasInt = int;
EntryPoint = "alias_add"
AliasAddNative(int value)
// Excluded markers
```

## autoWrapCallbacks

- Result property: `autoWrapCallbacks`
- Type: `bool`
- Source default: `false`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "delegatesAsVoidPointer": false,
  "strictSafetySeverity": "Warning",
  "autoWrapCallbacks": false
}
```

### Generated output assertions

```csharp
// Required markers
public unsafe delegate void LogCb(int level);
public unsafe delegate int SumCb(int left, int right);
public unsafe delegate void TickCb();
SetLogCallbackNative
SetSumCallbackNative
SetTickCallbackNative
Utils.GetFunctionPointerForDelegate(cb)
// Excluded markers
NativeCallback<LogCb>
NativeCallback<SumCb>
NativeCallback<TickCb>
__AutoWrapCallback_SetLogCallback_cb_0
```

## baseConfig

- Result property: `baseConfig`
- Type: `BaseConfig?`
- Source default: `null`
- Expected fixture value: `"Expected.FromBase"`

### Configuration

```json
{
  "baseConfig": {
    "url": "file://base.json"
  }
}
```

### Generated output assertions

```csharp
// Required markers
namespace Expected.FromBase
partial class BaseApi
internal const string LibName = "base-lib";
EntryPoint = "sample_add"
SampleAddNative
// Excluded markers
partial class EntryApi
internal const string LibName = "entry-lib";
```

## boolType

- Result property: `boolType`
- Type: `boolType`
- Source default: `BoolType.Bool8`
- Expected fixture value: `"Bool8"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "boolType": "Bool8"
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern Bool8 BoolEvalNative(Bool8 value);
public static bool BoolEval(bool value)
internal static extern Bool8 BoolAndNative(Bool8 left, Bool8 right);
public static bool BoolAnd(bool left, bool right)
ret != 0
left ? (Bool8)1 : (Bool8)0
right ? (Bool8)1 : (Bool8)0
// Excluded markers
internal static extern byte BoolEvalNative
internal static extern byte BoolAndNative
public static int BoolEval(
public static int BoolAnd(
```

## constantNamingConvention

- Result property: `constantNamingConvention`
- Type: `NamingConvention`
- Source default: `NamingConvention.Unknown`
- Expected fixture value: `"CamelCase"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "constantNamingConvention": "CamelCase"
}
```

### Generated output assertions

```csharp
// Required markers
public const int myFlag = 1;
public const int anotherValue = 2;
// Excluded markers
public const int MyFlag = 1;
public const int MY_FLAG = 1;
public const int AnotherValue = 2;
public const int ANOTHER_VALUE = 2;
```

## cppLogLevel

- Result property: `cppLogLevel`
- Type: `LogSeverity`
- Source default: `LogSeverity.Error`
- Expected fixture value: `"Error"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "cppLogLevel": "Error"
}
```

### Generated output assertions

```csharp
// Required markers
EntryPoint = "sample_add"
SampleAddNative
public static int SampleAdd(int a, int b)
// Excluded markers
```

## customEnums

- Result property: `customEnums`
- Type: `List<CsEnumMetadata>`
- Source default: `[]`
- Expected fixture value: `[{"identifier": "custom_mode", "cppName": "custom_mode", "name": "CustomMode", "attributes": [], "comment": null, "baseType": "int", "items": [{"identifier": "CUSTOM_MODE_ONE", "cppName": "CUSTOM_MODE_ONE", "cppValue": "1", "name": "One", "value": "1", "attributes": [], "comment": null}]}]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "customEnums": [
    {
      "cppName": "custom_mode",
      "name": "CustomMode",
      "attributes": [],
      "comment": "Plain enum comment",
      "baseType": "int",
      "items": [
        {
          "cppName": "CUSTOM_MODE_ONE",
          "cppValue": "1",
          "name": "One",
          "value": "1",
          "attributes": [],
          "comment": "Plain item comment"
        }
      ]
    }
  ]
}
```

### Generated output assertions

```csharp
// Required markers
/// Plain enum comment
/// Plain item comment
public enum CustomMode
// Excluded markers
```

## defines

- Result property: `defines`
- Type: `List<string>`
- Source default: `[]`
- Expected fixture value: `["MY_DEF=1"]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "defines": [
    "MY_DEF=1"
  ]
}
```

### Generated output assertions

```csharp
// Required markers
EntryPoint = "defined_fn"
DefinedFnNative
// Excluded markers
EntryPoint = "undefined_fn"
UndefinedFnNative
```

## delegateNamingConvention

- Result property: `delegateNamingConvention`
- Type: `NamingConvention`
- Source default: `NamingConvention.PascalCase`
- Expected fixture value: `"CamelCase"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "delegateNamingConvention": "CamelCase"
}
```

### Generated output assertions

```csharp
// Required markers
public unsafe delegate void sampleCallback(int value);
// Excluded markers
public unsafe delegate void SampleCallback(int value);
```

## delegatesAsVoidPointer

- Result property: `delegatesAsVoidPointer`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "delegatesAsVoidPointer": false
}
```

### Generated output assertions

```csharp
// Required markers
ApplyCbNative
SetNotifyNative
DispatchMixNative
delegate* unmanaged[Cdecl]<int, int> cb
delegate* unmanaged[Cdecl]<void> cb
delegate* unmanaged[
// Excluded markers
void* cb
```

## enableExperimentalOptions

- Result property: `enableExperimentalOptions`
- Type: `bool`
- Source default: `false`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "delegatesAsVoidPointer": false,
  "enableExperimentalOptions": false
}
```

### Generated output assertions

```csharp
// Required markers
ApplyCbNative
delegate* unmanaged[Cdecl]<int, int> cb
// Excluded markers
void* cb
```

## enumItemNamingConvention

- Result property: `enumItemNamingConvention`
- Type: `NamingConvention`
- Source default: `NamingConvention.PascalCase`
- Expected fixture value: `"CamelCase"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "enumItemNamingConvention": "CamelCase"
}
```

### Generated output assertions

```csharp
// Required markers
enum ColorMode
one = unchecked(1)
two = unchecked(2)
// Excluded markers
One = unchecked(1)
Two = unchecked(2)
```

## enumNamingConvention

- Result property: `enumNamingConvention`
- Type: `NamingConvention`
- Source default: `NamingConvention.PascalCase`
- Expected fixture value: `"CamelCase"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "enumNamingConvention": "CamelCase"
}
```

### Generated output assertions

```csharp
// Required markers
public enum sampleMode
// Excluded markers
public enum SampleMode
```

## extensionNamingConvention

- Result property: `extensionNamingConvention`
- Type: `NamingConvention`
- Source default: `NamingConvention.PascalCase`
- Expected fixture value: `"CamelCase"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "wrapPointersAsHandle": true,
  "generateExtensions": true,
  "generateFunctions": true,
  "generateRuntimeSource": false,
  "extensionNamingConvention": "CamelCase"
}
```

### Generated output assertions

```csharp
// Required markers
partial class Extensions
public static void setValue(this
// Excluded markers
public static void SetValue(this
```

## functionContainerMappings

- Result property: `functionContainerMappings`
- Type: `composition behavior`
- Source default: `see the selected configuration`
- Expected fixture value: `null`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "functionMappings": [
    {
      "exportedName": "internal_add",
      "friendlyName": "Add",
      "containerName": "EntryInternals",
      "defaults": {},
      "customVariations": []
    }
  ]
}
```

## functionNamingConvention

- Result property: `functionNamingConvention`
- Type: `NamingConvention`
- Source default: `NamingConvention.PascalCase`
- Expected fixture value: `"CamelCase"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "functionNamingConvention": "CamelCase"
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int sampleAddNative(int a, int b);
public static int sampleAdd(int a, int b)
// Excluded markers
public static int SampleAdd(int a, int b)
```

## functionTableEntries

- Result property: `functionTableEntries`
- Type: `List<CsFunctionTableEntry>`
- Source default: `[]`
- Expected fixture value: `[{"index": 7, "entryPoint": "sample_add"}]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "FunctionTable",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "functionTableEntries": [
    {
      "entryPoint": "sample_add"
    },
    {
      "entryPoint": "sample_sub"
    }
  ]
}
```

### Generated output assertions

```csharp
// Required markers
candidate.LoadRequired(0, "sample_add");
candidate.LoadRequired(1, "sample_sub");
// Excluded markers
candidate.LoadRequired(0, "sample_sub");
```

## generateAdditionalOverloads

- Result property: `generateAdditionalOverloads`
- Type: `bool`
- Source default: `false`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateAdditionalOverloads": false
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// Excluded markers
```

## generateConstants

- Result property: `generateConstants`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateConstants": false
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// Excluded markers
public const int SAMPLE_FLAG = 7;
```

## generateConstructorsForStructs

- Result property: `generateConstructorsForStructs`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `true`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateConstructorsForStructs": true
}
```

### Generated output assertions

```csharp
// Required markers
public partial struct SampleVec2
public int X;
public int Y;
public unsafe SampleVec2(
// Excluded markers
```

## generateDelegates

- Result property: `generateDelegates`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "delegatesAsVoidPointer": false,
  "generateDelegates": false
}
```

### Generated output assertions

```csharp
// Required markers
namespace EntryTests.Generated
partial class EntryApi
SetCallbackNative
// Excluded markers
public unsafe delegate void SampleCb
```

## generateEnums

- Result property: `generateEnums`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateEnums": false
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// Excluded markers
public enum SampleMode
```

## generateExtensions

- Result property: `generateExtensions`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "wrapPointersAsHandle": true,
  "generateExtensions": false,
  "generateFunctions": true,
  "generateRuntimeSource": false
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern void ExtHandleSetValueInterop(nint handle, int value);
internal static void ExtHandleSetValueNative(ExtHandle handle, int value)
ExtHandleSetValueInterop(handle.Handle, value);
public static void ExtHandleSetValue(ExtHandle handle, int value)
// Excluded markers
partial class Extensions
```

## generateFunctions

- Result property: `generateFunctions`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateFunctions": false
}
```

### Generated output assertions

```csharp
// Required markers
// Excluded markers
DllImport(LibName
SampleAddNative
SampleAdd(
```

## generateHandles

- Result property: `generateHandles`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `true`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateHandles": true
}
```

### Generated output assertions

```csharp
// Required markers
SampleAddNative
readonly partial struct GenHandle
// Excluded markers
```

## generateMetadata

- Result property: `generateMetadata`
- Type: `bool`
- Source default: `false`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateMetadata": false
}
```

### Generated output assertions

```csharp
// Required markers
[DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sample_add")]
// Excluded markers
[NativeName(
```

## generatePlaceholderComments

- Result property: `generatePlaceholderComments`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generatePlaceholderComments": false
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// Excluded markers
To be documented.
```

## generateRuntimeSource

- Result property: `generateRuntimeSource`
- Type: `bool`
- Source default: `false`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// Excluded markers
```

## generateSizeOfStructs

- Result property: `generateSizeOfStructs`
- Type: `bool`
- Source default: `false`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateSizeOfStructs": false
}
```

### Generated output assertions

```csharp
// Required markers
public partial struct SampleSizeType
// Excluded markers
public static readonly int SizeInBytes = 
```

## generateTypes

- Result property: `generateTypes`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `true`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateTypes": true
}
```

### Generated output assertions

```csharp
// Required markers
public partial struct SamplePointType
public int X;
public int Y;
// Excluded markers
```

## getLibraryExtensionFunctionName

- Result property: `getLibraryExtensionFunctionName`
- Type: `string?`
- Source default: `null`
- Expected fixture value: `"GetLibraryExtXAlt"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "getLibraryExtensionFunctionName": "GetLibraryExtXAlt"
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// Excluded markers
```

## getLibraryNameFunctionName

- Result property: `getLibraryNameFunctionName`
- Type: `string`
- Source default: `"GetLibraryName"`
- Expected fixture value: `"GetLibraryNameXAlt"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "getLibraryNameFunctionName": "GetLibraryNameXAlt"
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// Excluded markers
```

## handleNamingConvention

- Result property: `handleNamingConvention`
- Type: `NamingConvention`
- Source default: `NamingConvention.PascalCase`
- Expected fixture value: `"PascalCase"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "handleNamingConvention": "PascalCase"
}
```

### Generated output assertions

```csharp
// Required markers
public readonly partial struct WidgetHandle : IEquatable<WidgetHandle>
internal static extern void WidgetHandleReleaseInterop(nint handle);
internal static void WidgetHandleReleaseNative(WidgetHandle handle)
WidgetHandleReleaseInterop(handle.Handle);
public static void WidgetHandleRelease(WidgetHandle handle)
// Excluded markers
widget_handle_t
```

## ignoredConstants

- Result property: `ignoredConstants`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "ignoredConstants": []
}
```

### Generated output assertions

```csharp
// Required markers
public const int MY_CONST = 9;
public const int KEEP_CONST = 13;
internal static extern int ReadConstNative();
// Excluded markers
```

## ignoredDelegates

- Result property: `ignoredDelegates`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "ignoredDelegates": []
}
```

### Generated output assertions

```csharp
// Required markers
public unsafe delegate void MyDelegate(int value);
public unsafe delegate void KeepDelegate(int value);
internal static extern void SetKeepDelegateNative(
// Excluded markers
```

## ignoredEnums

- Result property: `ignoredEnums`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "ignoredEnums": []
}
```

### Generated output assertions

```csharp
// Required markers
public enum MyEnum : int
public enum KeepEnum : int
internal static extern int UseEnumNative(KeepEnum mode);
// Excluded markers
```

## ignoredExtensions

- Result property: `ignoredExtensions`
- Type: `HashSet<string>`
- Source default: `new()`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": true,
  "generateRuntimeSource": false,
  "ignoredExtensions": []
}
```

### Generated output assertions

```csharp
// Required markers
public static unsafe partial class Extensions
public static void KeepExt(this WidgetHandle handle, int value)
public static void DropExt(this WidgetHandle handle, int value)
internal static extern void KeepExtInterop(nint handle, int value);
internal static extern void DropExtInterop(nint handle, int value);
internal static void KeepExtNative(WidgetHandle handle, int value)
internal static void DropExtNative(WidgetHandle handle, int value)
KeepExtInterop(handle.Handle, value);
DropExtInterop(handle.Handle, value);
// Excluded markers
```

## ignoredFunctions

- Result property: `ignoredFunctions`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "ignoredFunctions": []
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int KeepFnNative(int a, int b);
internal static extern int DropFnNative(int a, int b);
public static int KeepFn(int a, int b)
public static int DropFn(int a, int b)
// Excluded markers
```

## ignoredParts

- Result property: `ignoredParts`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "ignoredParts": []
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int EntrySampleAddNative(int a, int b);
public static int EntrySampleAdd(int a, int b)
// Excluded markers
```

## ignoredTypedefs

- Result property: `ignoredTypedefs`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "autoSquashTypedef": false,
  "ignoredTypedefs": []
}
```

### Generated output assertions

```csharp
// Required markers
using MyTypedef = int;
using KeepTypedef = int;
internal static extern int AddKeepNative(int value);
// Excluded markers
```

## ignoredTypes

- Result property: `ignoredTypes`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "ignoredTypes": []
}
```

### Generated output assertions

```csharp
// Required markers
public partial struct MyType
public partial struct KeepType
UseTypesNative(
// Excluded markers
```

## importType

- Result property: `importType`
- Type: `importType`
- Source default: `ImportType.FunctionTable`
- Expected fixture value: `"DllImport"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false
}
```

### Generated output assertions

```csharp
// Required markers
[DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sample_add")]
internal static extern int SampleAddNative(int a, int b);
// Excluded markers
internal static global::BGCS.Runtime.FunctionTable funcTable = null !;
```

## includeFolders

- Result property: `includeFolders`
- Type: `List<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "includeFolders": []
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int UseDepNative(
public static int UseDep(
// Excluded markers
```

## keywords

- Result property: `keywords`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "keywords": []
}
```

### Generated output assertions

```csharp
// Required markers
public static int UseKeyword(int customKeyword)
// Excluded markers
public static int UseKeyword(int @customKeyword)
```

## knownConstantNames

- Result property: `knownConstantNames`
- Type: `Dictionary<string, string>`
- Source default: `{}`
- Expected fixture value: `{}`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "knownConstantNames": {}
}
```

### Generated output assertions

```csharp
// Required markers
public const int SAMPLE_CONST = 7;
public const int KEEP_CONST = 3;
// Excluded markers
public const int SpecialConst = 7;
```

## knownConstructors

- Result property: `knownConstructors`
- Type: `Dictionary<string, List<string>>`
- Source default: `{}`
- Expected fixture value: `{}`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "knownConstructors": {}
}
```

### Generated output assertions

```csharp
// Required markers
public partial struct MyType
internal static extern MyType MyTypeCreateNative(int value);
public static MyType MyTypeCreate(int value)
// Excluded markers
```

## knownDefaultValueNames

- Result property: `knownDefaultValueNames`
- Type: `Dictionary<string, string>`
- Source default: `{}`
- Expected fixture value: `{}`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "functionMappings": [
    {
      "exportedName": "set_mode",
      "friendlyName": null,
      "comment": null,
      "defaults": {
        "mode": "42"
      },
      "customVariations": [],
      "parameters": null
    }
  ],
  "knownDefaultValueNames": {}
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SetModeNative(int mode);
public static int SetMode()
return SetMode((int)(42));
// Excluded markers
return SetMode((int)(7));
```

## knownEnumPrefixes

- Result property: `knownEnumPrefixes`
- Type: `Dictionary<string, string>`
- Source default: `{}`
- Expected fixture value: `{}`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "knownEnumPrefixes": {}
}
```

### Generated output assertions

```csharp
// Required markers
public enum StrangeEnum : int
MyPrefixOff = unchecked(0)
MyPrefixOn = unchecked(1)
// Excluded markers
```

## knownEnumValueNames

- Result property: `knownEnumValueNames`
- Type: `Dictionary<string, string>`
- Source default: `{}`
- Expected fixture value: `{}`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "knownEnumValueNames": {}
}
```

### Generated output assertions

```csharp
// Required markers
public enum StrangeEnum : int
MyPrefixOff = unchecked(0)
MyPrefixOn = unchecked(1)
// Excluded markers
```

## knownExtensionNames

- Result property: `knownExtensionNames`
- Type: `Dictionary<string, string>`
- Source default: `{}`
- Expected fixture value: `{}`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": true,
  "generateRuntimeSource": false,
  "knownExtensionNames": {}
}
```

### Generated output assertions

```csharp
// Required markers
public static unsafe partial class Extensions
public static void SetValue(this WidgetHandle handle, int value)
// Excluded markers
public static void ApplyValue(this WidgetHandle handle, int value)
```

## knownExtensionPrefixes

- Result property: `knownExtensionPrefixes`
- Type: `Dictionary<string, string>`
- Source default: `{}`
- Expected fixture value: `{}`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": true,
  "generateRuntimeSource": false,
  "knownExtensionPrefixes": {}
}
```

### Generated output assertions

```csharp
// Required markers
public static unsafe partial class Extensions
public static void LibSetValue(this WidgetHandle handle, int value)
// Excluded markers
public static void SetValue(this WidgetHandle handle, int value)
```

## knownMemberFunctions

- Result property: `knownMemberFunctions`
- Type: `Dictionary<string, List<string>>`
- Source default: `{}`
- Expected fixture value: `{}`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "knownMemberFunctions": {}
}
```

### Generated output assertions

```csharp
// Required markers
public partial struct MyType
internal static extern int MyTypeIncNative(MyType* self, int delta);
// Excluded markers
public unsafe int MyTypeInc(int delta)
```

## libName

- Result property: `libName`
- Type: `string`
- Source default: `string.Empty`
- Expected fixture value: `"entry-lib-xAlt"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib-xAlt",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false
}
```

### Generated output assertions

```csharp
// Required markers
internal const string LibName = "entry-lib-xAlt";
[DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sample_add")]
// Excluded markers
```

## logLevel

- Result property: `logLevel`
- Type: `LogSeverity`
- Source default: `LogSeverity.Warning`
- Expected fixture value: `"Warning"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "logLevel": "Warning"
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleAddNative(int a, int b);
internal static extern int SampleSubNative(int a, int b);
public static int SampleAdd(int a, int b)
public static int SampleSub(int a, int b)
// Excluded markers
```

## memberNamingConvention

- Result property: `memberNamingConvention`
- Type: `NamingConvention`
- Source default: `NamingConvention.PascalCase`
- Expected fixture value: `"PascalCase"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "memberNamingConvention": "PascalCase"
}
```

### Generated output assertions

```csharp
// Required markers
public partial struct SampleType
public int ValueOne;
public int ValueTwo;
internal static extern int SampleUseNative(SampleType value);
// Excluded markers
```

## mergeGeneratedFilesToSingleFile

- Result property: `mergeGeneratedFilesToSingleFile`
- Type: `bool`
- Source default: `false`
- Expected fixture value: `true`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "singleFileOutputName": "Example.Native.Generated.cs",
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false
}
```

### Generated output assertions

```csharp
// Required markers
public partial struct MergeType
public enum MergeMode : int
internal static extern int SampleAddNative(int a, int b);
internal static extern void MergeSetModeNative(MergeType* data, MergeMode mode);
// Excluded markers
```

## namespace

- Result property: `namespace`
- Type: `string`
- Source default: `string.Empty`
- Expected fixture value: `"Expected.Namespace.Entry.Alt"`

### Configuration

```json
{
  "namespace": "Expected.Namespace.Entry.Alt",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false
}
```

### Generated output assertions

```csharp
// Required markers
namespace Expected.Namespace.Entry.Alt
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// Excluded markers
```

## oneFilePerType

- Result property: `oneFilePerType`
- Type: `bool`
- Source default: `true`
- Expected fixture value: `true`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": false,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "oneFilePerType": true
}
```

### Generated output assertions

```csharp
// Required markers
// Excluded markers
```

## parameterNamingConvention

- Result property: `parameterNamingConvention`
- Type: `NamingConvention`
- Source default: `NamingConvention.CamelCase`
- Expected fixture value: `"CamelCase"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "parameterNamingConvention": "CamelCase"
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleAddNative(int inputValue, int maxCount);
public static int SampleAdd(int inputValue, int maxCount)
// Excluded markers
SampleAddNative(int InputValue, int MaxCount)
```

## runtimeNamespace

- Result property: `runtimeNamespace`
- Type: `string`
- Source default: `string.Empty`
- Expected fixture value: `"EntryTests.Runtime.Alt"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": true,
  "runtimeNamespace": "EntryTests.Runtime.Alt"
}
```

### Generated output assertions

```csharp
// Required markers
using EntryTests.Runtime.Alt;
internal static extern Bool8 BoolEvalNative(Bool8 value);
public static bool BoolEval(bool value)
// Excluded markers
```

## systemIncludeFolders

- Result property: `systemIncludeFolders`
- Type: `List<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "systemIncludeFolders": []
}
```

### Generated output assertions

```csharp
// Required markers
// Excluded markers
```

## typeFieldMappings

- Result property: `typeFieldMappings`
- Type: `composition behavior`
- Source default: `see the selected configuration`
- Expected fixture value: `null`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "memberNamingConvention": "PascalCase",
  "classMappings": [
    {
      "exportedName": "sample_type",
      "friendlyName": "SampleType",
      "fieldMappings": [
        {
          "exportedName": "id",
          "displayName": "ID"
        },
        {
          "exportedName": "url",
          "displayName": "URL"
        },
        {
          "exportedName": "cpu",
          "displayName": "CPU"
        }
      ]
    }
  ]
}
```

## typeNamingConvention

- Result property: `typeNamingConvention`
- Type: `NamingConvention`
- Source default: `NamingConvention.PascalCase`
- Expected fixture value: `"PascalCase"`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "typeNamingConvention": "PascalCase"
}
```

### Generated output assertions

```csharp
// Required markers
public partial struct SampleType
internal static extern SampleType MakeSampleNative(int value);
public static SampleType MakeSample(int value)
// Excluded markers
```

## useCustomContext

- Result property: `useCustomContext`
- Type: `bool`
- Source default: `false`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "FunctionTable",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "useCustomContext": false
}
```

### Generated output assertions

```csharp
// Required markers
internal static global::BGCS.Runtime.FunctionTable funcTable = null !;
private static string GetLibraryName()
public static void InitApi()
var candidate = new global::BGCS.Runtime.FunctionTable(global::BGCS.Runtime.LibraryLoader.LoadLibrary(GetLibraryName, null), 1);
candidate.LoadRequired(0, "sample_add");
// Excluded markers
public static void InitApi(global::BGCS.Runtime.INativeContext context)
```

## usings

- Result property: `usings`
- Type: `List<string>`
- Source default: `[]`
- Expected fixture value: `[]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "usings": []
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleAddNative(int a, int b);
// Excluded markers
using System.Text;
```

## varyingTypes

- Result property: `varyingTypes`
- Type: `HashSet<string>`
- Source default: `[]`
- Expected fixture value: `["ReadOnlySpan<byte>", "string", "ref string"]`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "generateAdditionalOverloads": true,
  "varyingTypes": []
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern void SetNameNative(byte* name);
public static void SetName(in byte name)
public static void SetName(string name)
public static void SetName(ReadOnlySpan<byte> name)
// Excluded markers
```

## wrapPointersAsHandle

- Result property: `wrapPointersAsHandle`
- Type: `bool`
- Source default: `false`
- Expected fixture value: `false`

### Configuration

```json
{
  "namespace": "EntryTests.Generated",
  "apiName": "EntryApi",
  "libName": "entry-lib",
  "mergeGeneratedFilesToSingleFile": true,
  "importType": "DllImport",
  "enableExperimentalOptions": true,
  "generateExtensions": false,
  "generateRuntimeSource": false,
  "wrapPointersAsHandle": false
}
```

### Generated output assertions

```csharp
// Required markers
internal static extern int SampleTakePtrNative(SampleType* value);
public static int SampleTakePtr(SampleType* value)
// Excluded markers
public unsafe struct SampleTypePtr : IEquatable<SampleTypePtr>
```
