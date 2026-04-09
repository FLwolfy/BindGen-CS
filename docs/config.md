# Configuration

This is an entry-by-entry specification generated from `BGCS.Configuration.Tests`.

## AdditionalArguments

### 1. Explanation
**AdditionalArguments** controls the **AdditionalArguments** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `List<string>`
- Default value: `[]`
- Example expected value: `["-DFROM_ADDITIONAL_ARGUMENTS=1"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "AdditionalArguments": [
    "-DFROM_ADDITIONAL_ARGUMENTS=1"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
EntryPoint = "args_ok"
ArgsOkNative
partial class EntryApi
// NotContains
```

## AllowedConstants

### 1. Explanation
**AllowedConstants** controls the **AllowedConstants** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["KEEP_CONST"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateMetadata": true,
  "AllowedConstants": [
    "KEEP_CONST"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
KEEP_CONST
// NotContains
DROP_CONST
```

## AllowedDelegates

### 1. Explanation
**AllowedDelegates** controls the **AllowedDelegates** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["KeepDelegate"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "AllowedDelegates": [
    "KeepDelegate"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
delegate void KeepDelegate(
// NotContains
delegate void DropDelegate(
```

## AllowedEnums

### 1. Explanation
**AllowedEnums** controls the **AllowedEnums** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["KeepEnum"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "AllowedEnums": [
    "KeepEnum"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
enum KeepEnum
// NotContains
enum DropEnum
```

## AllowedExtensions

### 1. Explanation
**AllowedExtensions** controls the **AllowedExtensions** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["keep_ext"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": true,
  "GenerateRuntimeSource": false,
  "GenerateFunctions": true,
  "AllowedExtensions": [
    "keep_ext"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
partial class Extensions
public static void KeepExt(this
// NotContains
public static void DropExt(this
```

## AllowedFunctions

### 1. Explanation
**AllowedFunctions** controls the **AllowedFunctions** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["keep_fn"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "AllowedFunctions": [
    "keep_fn"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
EntryPoint = "keep_fn"
KeepFnNative
// NotContains
drop_fn
DropFnNative
```

## AllowedTypedefs

### 1. Explanation
**AllowedTypedefs** controls the **AllowedTypedefs** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["KeepHandle"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "AllowedTypedefs": [
    "KeepHandle"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
partial struct KeepHandle
// NotContains
partial struct DropHandle
```

## AllowedTypes

### 1. Explanation
**AllowedTypes** controls the **AllowedTypes** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["KeepType"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "AllowedTypes": [
    "KeepType"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
partial struct KeepType
// NotContains
partial struct DropType
```

## ApiName

### 1. Explanation
**ApiName** controls the **ApiName** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `string`
- Default value: `string.Empty`
- Example expected value: `"ExpectedApiName"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "ExpectedApiName",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false
}
```

#### Example generated output markers
```csharp
// Contains
partial class ExpectedApiName
ApiNameFnNative
// NotContains
partial class EntryApi
```

## AutoSquashTypedef

### 1. Explanation
**AutoSquashTypedef** controls the **AutoSquashTypedef** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "AutoSquashTypedef": false
}
```

#### Example generated output markers
```csharp
// Contains
using BaseInt = int;
using AliasInt = int;
EntryPoint = "alias_add"
AliasAddNative(AliasInt value)
// NotContains
AliasAddNative(int value)
```

## AutoWrapCallbacks

### 1. Explanation
**AutoWrapCallbacks** controls the **AutoWrapCallbacks** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `false`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "DelegatesAsVoidPointer": false,
  "AutoWrapCallbacks": false
}
```

#### Example generated output markers
```csharp
// Contains
public unsafe delegate void LogCb(int level);
public unsafe delegate int SumCb(int left, int right);
public unsafe delegate void TickCb();
SetLogCallbackNative
SetSumCallbackNative
SetTickCallbackNative
Utils.GetFunctionPointerForDelegate(cb)
// NotContains
NativeCallback<LogCb>
NativeCallback<SumCb>
NativeCallback<TickCb>
__AutoWrapCallback_SetLogCallback_cb_0
```

## BaseConfig

### 1. Explanation
**BaseConfig** is validated through composition behavior, and tests assert the final **Namespace** after **BaseConfig** is applied.

### 2. Type, Example, and Default Value
- Type: `BaseConfig?`
- Default value: `null`
- Example expected value: `"Expected.FromBase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "BaseConfig": {
    "Url": "file://base.json"
  }
}
```

#### Example generated output markers
```csharp
// Contains
namespace Expected.FromBase
partial class BaseApi
internal const string LibName = "base-lib";
EntryPoint = "sample_add"
SampleAddNative
// NotContains
partial class EntryApi
internal const string LibName = "entry-lib";
```

## BoolType

### 1. Explanation
**BoolType** controls the **BoolType** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `BoolType`
- Default value: `BoolType.Bool8`
- Example expected value: `"Bool32"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "BoolType": "Bool32"
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern Bool32 BoolEvalNative(Bool32 value);
public static bool BoolEval(bool value)
internal static extern Bool32 BoolAndNative(Bool32 left, Bool32 right);
public static bool BoolAnd(bool left, bool right)
ret != 0
left ? (Bool32)1 : (Bool32)0
right ? (Bool32)1 : (Bool32)0
// NotContains
internal static extern int BoolEvalNative
internal static extern int BoolAndNative
public static int BoolEval(
public static int BoolAnd(
```

## ConstantNamingConvention

### 1. Explanation
**ConstantNamingConvention** controls the **ConstantNamingConvention** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `NamingConvention`
- Default value: `NamingConvention.Unknown`
- Example expected value: `"PascalCase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "ConstantNamingConvention": "PascalCase"
}
```

#### Example generated output markers
```csharp
// Contains
public const int MyFlag = 1;
public const int AnotherValue = 2;
// NotContains
public const int myFlag = 1;
public const int MY_FLAG = 1;
public const int anotherValue = 2;
public const int ANOTHER_VALUE = 2;
```

## CppLogLevel

### 1. Explanation
**CppLogLevel** controls the **CppLogLevel** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `LogSeverity`
- Default value: `LogSeverity.Error`
- Example expected value: `"Warning"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "CppLogLevel": "Warning"
}
```

#### Example generated output markers
```csharp
// Contains
EntryPoint = "sample_add"
SampleAddNative
public static int SampleAdd(int a, int b)
// NotContains
```

## CustomEnums

### 1. Explanation
**CustomEnums** controls the **CustomEnums** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `List<CsEnumMetadata>`
- Default value: `[]`
- Example expected value: `[{"Identifier":"custom_mode","CppName":"custom_mode","Name":"CustomMode","Attributes":[],"Comment":null,"BaseType":"int","Items":[{"Identifier":"CUSTOM_MODE_ONE","CppName":"CUSTOM_MODE_ONE","CppValue":"1","Name":"One","Value":"1","Attributes":[],"Comment":null}]}]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "CustomEnums": [
    {
      "CppName": "custom_mode",
      "Name": "CustomMode",
      "Attributes": [],
      "Comment": null,
      "BaseType": "int",
      "Items": [
        {
          "CppName": "CUSTOM_MODE_ONE",
          "CppValue": "1",
          "Name": "One",
          "Value": "1",
          "Attributes": [],
          "Comment": null
        }
      ]
    }
  ]
}
```

#### Example generated output markers
```csharp
// Contains
public enum CustomMode
One = 1
// NotContains
enum custom_mode
```

## Defines

### 1. Explanation
**Defines** controls the **Defines** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `List<string>`
- Default value: `[]`
- Example expected value: `["MY_DEF=1"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "Defines": [
    "MY_DEF=1"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
EntryPoint = "defined_fn"
DefinedFnNative
// NotContains
EntryPoint = "undefined_fn"
UndefinedFnNative
```

## DelegateNamingConvention

### 1. Explanation
**DelegateNamingConvention** controls the **DelegateNamingConvention** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `NamingConvention`
- Default value: `NamingConvention.PascalCase`
- Example expected value: `"CamelCase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "DelegateNamingConvention": "CamelCase"
}
```

#### Example generated output markers
```csharp
// Contains
public unsafe delegate void sampleCallback(int value);
// NotContains
public unsafe delegate void SampleCallback(int value);
```

## DelegatesAsVoidPointer

### 1. Explanation
**DelegatesAsVoidPointer** controls the **DelegatesAsVoidPointer** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "DelegatesAsVoidPointer": false
}
```

#### Example generated output markers
```csharp
// Contains
ApplyCbNative
SetNotifyNative
DispatchMixNative
delegate*<int, int> cb
delegate*<void> cb
delegate*<
// NotContains
void* cb
```

## EnableExperimentalOptions

### 1. Explanation
**EnableExperimentalOptions** controls the **EnableExperimentalOptions** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `false`
- Example expected value: `true`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "DelegatesAsVoidPointer": false,
  "EnableExperimentalOptions": true
}
```

#### Example generated output markers
```csharp
// Contains
ApplyCbNative
delegate*<int, int> cb
// NotContains
void* cb
```

## EnumItemNamingConvention

### 1. Explanation
**EnumItemNamingConvention** controls the **EnumItemNamingConvention** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `NamingConvention`
- Default value: `NamingConvention.PascalCase`
- Example expected value: `"CamelCase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "EnumItemNamingConvention": "CamelCase"
}
```

#### Example generated output markers
```csharp
// Contains
enum ColorMode
one = unchecked(1)
two = unchecked(2)
// NotContains
One = unchecked(1)
Two = unchecked(2)
```

## EnumNamingConvention

### 1. Explanation
**EnumNamingConvention** controls the **EnumNamingConvention** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `NamingConvention`
- Default value: `NamingConvention.PascalCase`
- Example expected value: `"CamelCase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "EnumNamingConvention": "CamelCase"
}
```

#### Example generated output markers
```csharp
// Contains
public enum sampleMode
// NotContains
public enum SampleMode
```

## ExtensionNamingConvention

### 1. Explanation
**ExtensionNamingConvention** controls the **ExtensionNamingConvention** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `NamingConvention`
- Default value: `NamingConvention.PascalCase`
- Example expected value: `"CamelCase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "WrapPointersAsHandle": true,
  "GenerateExtensions": true,
  "GenerateFunctions": true,
  "GenerateRuntimeSource": false,
  "ExtensionNamingConvention": "CamelCase"
}
```

#### Example generated output markers
```csharp
// Contains
partial class Extensions
public static void setValue(this
// NotContains
public static void SetValue(this
```

## FunctionNamingConvention

### 1. Explanation
**FunctionNamingConvention** controls the **FunctionNamingConvention** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `NamingConvention`
- Default value: `NamingConvention.PascalCase`
- Example expected value: `"CamelCase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "FunctionNamingConvention": "CamelCase"
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int sampleAddNative(int a, int b);
public static int sampleAdd(int a, int b)
// NotContains
public static int SampleAdd(int a, int b)
```

## FunctionTableEntries

### 1. Explanation
**FunctionTableEntries** controls the **FunctionTableEntries** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `List<CsFunctionTableEntry>`
- Default value: `[]`
- Example expected value: `[{"Index":0,"EntryPoint":"sample_add"}]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "FunctionTable",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "FunctionTableEntries": [
    {
      "Index": 0,
      "EntryPoint": "sample_add"
    }
  ]
}
```

#### Example generated output markers
```csharp
// Contains
funcTable[0]
delegate* unmanaged[Cdecl]<int, int, int>
new FunctionTable(
// NotContains
[DllImport(
```

## GenerateAdditionalOverloads

### 1. Explanation
**GenerateAdditionalOverloads** controls the **GenerateAdditionalOverloads** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `false`
- Example expected value: `true`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateAdditionalOverloads": true
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// NotContains
```

## GenerateConstants

### 1. Explanation
**GenerateConstants** controls the **GenerateConstants** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateConstants": false
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// NotContains
public const int SAMPLE_FLAG = 7;
```

## GenerateConstructorsForStructs

### 1. Explanation
**GenerateConstructorsForStructs** controls the **GenerateConstructorsForStructs** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateConstructorsForStructs": false
}
```

#### Example generated output markers
```csharp
// Contains
public partial struct SampleVec2
public int X;
public int Y;
// NotContains
public unsafe SampleVec2(
```

## GenerateDelegates

### 1. Explanation
**GenerateDelegates** controls the **GenerateDelegates** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "DelegatesAsVoidPointer": false,
  "GenerateDelegates": false
}
```

#### Example generated output markers
```csharp
// Contains
namespace EntryTests.Generated
partial class EntryApi
SetCallbackNative
// NotContains
public unsafe delegate void SampleCb
```

## GenerateEnums

### 1. Explanation
**GenerateEnums** controls the **GenerateEnums** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateEnums": false
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// NotContains
public enum SampleMode
```

## GenerateExtensions

### 1. Explanation
**GenerateExtensions** controls the **GenerateExtensions** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `true`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "WrapPointersAsHandle": true,
  "GenerateExtensions": true,
  "GenerateFunctions": true,
  "GenerateRuntimeSource": false
}
```

#### Example generated output markers
```csharp
// Contains
partial class Extensions
public static void SetValue(this ExtHandle handle, int value)
// NotContains
public static void setValue(this ExtHandle handle, int value)
```

## GenerateFunctions

### 1. Explanation
**GenerateFunctions** controls the **GenerateFunctions** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateFunctions": false
}
```

#### Example generated output markers
```csharp
// Contains
// NotContains
DllImport(LibName
SampleAddNative
SampleAdd(
```

## GenerateHandles

### 1. Explanation
**GenerateHandles** controls the **GenerateHandles** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateHandles": false
}
```

#### Example generated output markers
```csharp
// Contains
SampleAddNative
// NotContains
readonly partial struct GenHandle
```

## GenerateMetadata

### 1. Explanation
**GenerateMetadata** controls the **GenerateMetadata** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `false`
- Example expected value: `true`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateMetadata": true
}
```

#### Example generated output markers
```csharp
// Contains
[NativeName(NativeNameType.Func, "sample_add")]
[return: NativeName(NativeNameType.Type, "int")]
[NativeName(NativeNameType.Param, "a")]
[DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sample_add")]
// NotContains
```

## GeneratePlaceholderComments

### 1. Explanation
**GeneratePlaceholderComments** controls the **GeneratePlaceholderComments** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GeneratePlaceholderComments": false
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// NotContains
To be documented.
```

## GenerateRuntimeSource

### 1. Explanation
**GenerateRuntimeSource** controls the **GenerateRuntimeSource** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `false`
- Example expected value: `true`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": true
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// NotContains
```

## GenerateSizeOfStructs

### 1. Explanation
**GenerateSizeOfStructs** controls the **GenerateSizeOfStructs** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `false`
- Example expected value: `true`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateSizeOfStructs": true
}
```

#### Example generated output markers
```csharp
// Contains
public partial struct SampleSizeType
public static readonly int SizeInBytes = 
// NotContains
```

## GenerateTypes

### 1. Explanation
**GenerateTypes** controls the **GenerateTypes** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateTypes": false
}
```

#### Example generated output markers
```csharp
// Contains
SampleAddNative
// NotContains
public partial struct SamplePointType
public int X;
public int Y;
```

## GetLibraryExtensionFunctionName

### 1. Explanation
**GetLibraryExtensionFunctionName** controls the **GetLibraryExtensionFunctionName** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `string?`
- Default value: `null`
- Example expected value: `"GetLibraryExtX"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GetLibraryExtensionFunctionName": "GetLibraryExtX"
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// NotContains
```

## GetLibraryNameFunctionName

### 1. Explanation
**GetLibraryNameFunctionName** controls the **GetLibraryNameFunctionName** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `string`
- Default value: `"GetLibraryName"`
- Example expected value: `"GetLibraryNameX"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GetLibraryNameFunctionName": "GetLibraryNameX"
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// NotContains
```

## HandleNamingConvention

### 1. Explanation
**HandleNamingConvention** controls the **HandleNamingConvention** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `NamingConvention`
- Default value: `NamingConvention.PascalCase`
- Example expected value: `"CamelCase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "HandleNamingConvention": "CamelCase"
}
```

#### Example generated output markers
```csharp
// Contains
public readonly partial struct WidgetHandle : IEquatable<WidgetHandle>
internal static extern void WidgetHandleReleaseNative(WidgetHandle handle);
public static void WidgetHandleRelease(WidgetHandle handle)
// NotContains
widget_handle_t
```

## HeaderInjector

### 1. Explanation
**HeaderInjector** controls the **HeaderInjector** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HeaderInjectionDelegate?`
- Default value: `null`
- Example expected value: `null`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false
}
```

#### Example generated output markers
```csharp
// Contains
public const int HEADER_FLAG = 11;
public enum HeaderMode : int
Off = unchecked(0)
On = unchecked(1)
internal static extern int HeaderEvalNative(HeaderMode mode);
// NotContains
```

## IgnoredConstants

### 1. Explanation
**IgnoredConstants** controls the **IgnoredConstants** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["MY_CONST"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "IgnoredConstants": [
    "MY_CONST"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
public const int KEEP_CONST = 13;
internal static extern int ReadConstNative();
// NotContains
public const int MY_CONST = 9;
```

## IgnoredDelegates

### 1. Explanation
**IgnoredDelegates** controls the **IgnoredDelegates** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["my_delegate"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "IgnoredDelegates": [
    "my_delegate"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
public unsafe delegate void KeepDelegate(int value);
internal static extern void SetKeepDelegateNative(
// NotContains
public unsafe delegate void MyDelegate(int value);
```

## IgnoredEnums

### 1. Explanation
**IgnoredEnums** controls the **IgnoredEnums** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["my_enum"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "IgnoredEnums": [
    "my_enum"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
public enum KeepEnum : int
internal static extern int UseEnumNative(KeepEnum mode);
// NotContains
public enum MyEnum : int
```

## IgnoredExtensions

### 1. Explanation
**IgnoredExtensions** controls the **IgnoredExtensions** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["drop_ext"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": true,
  "GenerateRuntimeSource": false,
  "IgnoredExtensions": [
    "drop_ext"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
public static unsafe partial class Extensions
public static void KeepExt(this WidgetHandle handle, int value)
internal static extern void KeepExtNative(WidgetHandle handle, int value);
internal static extern void DropExtNative(WidgetHandle handle, int value);
// NotContains
public static void DropExt(this WidgetHandle handle, int value)
```

## IgnoredFunctions

### 1. Explanation
**IgnoredFunctions** controls the **IgnoredFunctions** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["drop_fn"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "IgnoredFunctions": [
    "drop_fn"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int KeepFnNative(int a, int b);
public static int KeepFn(int a, int b)
// NotContains
DropFnNative
public static int DropFn(int a, int b)
```

## IgnoredParts

### 1. Explanation
**IgnoredParts** controls the **IgnoredParts** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["Entry"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "IgnoredParts": [
    "Entry"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// NotContains
EntrySampleAddNative
EntrySampleAdd(int a, int b)
```

## IgnoredTypedefs

### 1. Explanation
**IgnoredTypedefs** controls the **IgnoredTypedefs** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["my_typedef"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "AutoSquashTypedef": false,
  "IgnoredTypedefs": [
    "my_typedef"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
using KeepTypedef = int;
internal static extern KeepTypedef AddKeepNative(KeepTypedef value);
// NotContains
using MyTypedef = int;
```

## IgnoredTypes

### 1. Explanation
**IgnoredTypes** controls the **IgnoredTypes** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["my_type"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "IgnoredTypes": [
    "my_type"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
public partial struct KeepType
UseTypesNative(
// NotContains
public partial struct MyType
```

## ImportType

### 1. Explanation
**ImportType** controls the **ImportType** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `ImportType`
- Default value: `ImportType.FunctionTable`
- Example expected value: `"FunctionTable"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "FunctionTable",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false
}
```

#### Example generated output markers
```csharp
// Contains
internal static FunctionTable funcTable;
public static void InitApi()
funcTable.Load(0, "sample_add");
delegate* unmanaged[Cdecl]<int, int, int>)funcTable[0]
// NotContains
[DllImport(
```

## IncludeFolders

### 1. Explanation
**IncludeFolders** controls the **IncludeFolders** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `List<string>`
- Default value: `[]`
- Example expected value: `["include"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "IncludeFolders": [
    "include"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int UseDepNative(
public static int UseDep(
// NotContains
```

## Keywords

### 1. Explanation
**Keywords** controls the **Keywords** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["customKeyword"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "Keywords": [
    "customKeyword"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
public static int UseKeyword(int @customKeyword)
// NotContains
public static int UseKeyword(int customKeyword)
```

## KnownConstantNames

### 1. Explanation
**KnownConstantNames** controls the **KnownConstantNames** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `Dictionary<string, string>`
- Default value: `{}`
- Example expected value: `{"SAMPLE_CONST":"SpecialConst"}`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "KnownConstantNames": {
    "SAMPLE_CONST": "SpecialConst"
  }
}
```

#### Example generated output markers
```csharp
// Contains
public const int SpecialConst = 7;
public const int KEEP_CONST = 3;
// NotContains
public const int SAMPLE_CONST = 7;
```

## KnownConstructors

### 1. Explanation
**KnownConstructors** controls the **KnownConstructors** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `Dictionary<string, List<string>>`
- Default value: `{}`
- Example expected value: `{"my_type":["my_type_create"]}`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "KnownConstructors": {
    "my_type": [
      "my_type_create"
    ]
  }
}
```

#### Example generated output markers
```csharp
// Contains
public partial struct MyType
internal static extern MyType MyTypeCreateNative(int value);
public static MyType MyTypeCreate(int value)
// NotContains
```

## KnownDefaultValueNames

### 1. Explanation
**KnownDefaultValueNames** controls the **KnownDefaultValueNames** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `Dictionary<string, string>`
- Default value: `{}`
- Example expected value: `{"42":"7"}`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "FunctionMappings": [
    {
      "ExportedName": "set_mode",
      "FriendlyName": null,
      "Comment": null,
      "Defaults": {
        "mode": "42"
      },
      "CustomVariations": [],
      "Parameters": null
    }
  ],
  "KnownDefaultValueNames": {
    "42": "7"
  }
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SetModeNative(int mode);
public static int SetMode()
SetModeNative((int)(7));
// NotContains
SetModeNative((int)(42));
```

## KnownEnumPrefixes

### 1. Explanation
**KnownEnumPrefixes** controls the **KnownEnumPrefixes** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `Dictionary<string, string>`
- Default value: `{}`
- Example expected value: `{"strange_enum":"MY_PREFIX"}`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "KnownEnumPrefixes": {
    "strange_enum": "MY_PREFIX"
  }
}
```

#### Example generated output markers
```csharp
// Contains
public enum StrangeEnum : int
Off = unchecked(0)
On = unchecked(1)
// NotContains
MyPrefixOff
MyPrefixOn
```

## KnownEnumValueNames

### 1. Explanation
**KnownEnumValueNames** controls the **KnownEnumValueNames** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `Dictionary<string, string>`
- Default value: `{}`
- Example expected value: `{"MY_PREFIX_ON":"Enabled"}`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "KnownEnumValueNames": {
    "MY_PREFIX_ON": "Enabled"
  }
}
```

#### Example generated output markers
```csharp
// Contains
public enum StrangeEnum : int
MyPrefixOff = unchecked(0)
MyPrefixOn = unchecked(1)
// NotContains
```

## KnownExtensionNames

### 1. Explanation
**KnownExtensionNames** controls the **KnownExtensionNames** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `Dictionary<string, string>`
- Default value: `{}`
- Example expected value: `{"WidgetHandleSetValue":"ApplyValue"}`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": true,
  "GenerateRuntimeSource": false,
  "KnownExtensionNames": {
    "WidgetHandleSetValue": "ApplyValue"
  }
}
```

#### Example generated output markers
```csharp
// Contains
public static unsafe partial class Extensions
public static void ApplyValue(this WidgetHandle handle, int value)
// NotContains
public static void SetValue(this WidgetHandle handle, int value)
```

## KnownExtensionPrefixes

### 1. Explanation
**KnownExtensionPrefixes** controls the **KnownExtensionPrefixes** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `Dictionary<string, string>`
- Default value: `{}`
- Example expected value: `{"widget_handle_t":"LIB"}`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": true,
  "GenerateRuntimeSource": false,
  "KnownExtensionPrefixes": {
    "widget_handle_t": "LIB"
  }
}
```

#### Example generated output markers
```csharp
// Contains
public static unsafe partial class Extensions
public static void SetValue(this WidgetHandle handle, int value)
// NotContains
public static void LibSetValue(this WidgetHandle handle, int value)
```

## KnownMemberFunctions

### 1. Explanation
**KnownMemberFunctions** controls the **KnownMemberFunctions** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `Dictionary<string, List<string>>`
- Default value: `{}`
- Example expected value: `{"my_type":["my_type_inc"]}`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "KnownMemberFunctions": {
    "my_type": [
      "my_type_inc"
    ]
  }
}
```

#### Example generated output markers
```csharp
// Contains
public partial struct MyType
internal static extern int MyTypeIncNative(MyType* self, int delta);
public unsafe int MyTypeInc(int delta)
// NotContains
```

## LibName

### 1. Explanation
**LibName** controls the **LibName** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `string`
- Default value: `string.Empty`
- Example expected value: `"entry-lib-x"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib-x",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false
}
```

#### Example generated output markers
```csharp
// Contains
internal const string LibName = "entry-lib-x";
[DllImport(LibName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sample_add")]
// NotContains
```

## LogLevel

### 1. Explanation
**LogLevel** controls the **LogLevel** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `LogSeverity`
- Default value: `LogSeverity.Warning`
- Example expected value: `"Information"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "LogLevel": "Information"
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SampleAddNative(int a, int b);
internal static extern int SampleSubNative(int a, int b);
public static int SampleAdd(int a, int b)
public static int SampleSub(int a, int b)
// NotContains
```

## MemberNamingConvention

### 1. Explanation
**MemberNamingConvention** controls the **MemberNamingConvention** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `NamingConvention`
- Default value: `NamingConvention.PascalCase`
- Example expected value: `"CamelCase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "MemberNamingConvention": "CamelCase"
}
```

#### Example generated output markers
```csharp
// Contains
public partial struct SampleType
public int ValueOne;
public int ValueTwo;
internal static extern int SampleUseNative(SampleType value);
// NotContains
```

## MergeGeneratedFilesToSingleFile

### 1. Explanation
**MergeGeneratedFilesToSingleFile** controls the **MergeGeneratedFilesToSingleFile** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `false`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": false,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false
}
```

#### Example generated output markers
```csharp
// Contains
// NotContains
```

## Namespace

### 1. Explanation
**Namespace** controls the **Namespace** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `string`
- Default value: `string.Empty`
- Example expected value: `"Expected.Namespace.Entry"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "Expected.Namespace.Entry",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false
}
```

#### Example generated output markers
```csharp
// Contains
namespace Expected.Namespace.Entry
internal static extern int SampleAddNative(int a, int b);
public static int SampleAdd(int a, int b)
// NotContains
```

## OneFilePerType

### 1. Explanation
**OneFilePerType** controls the **OneFilePerType** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `true`
- Example expected value: `false`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": false,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "OneFilePerType": false
}
```

#### Example generated output markers
```csharp
// Contains
// NotContains
```

## ParameterNamingConvention

### 1. Explanation
**ParameterNamingConvention** controls the **ParameterNamingConvention** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `NamingConvention`
- Default value: `NamingConvention.CamelCase`
- Example expected value: `"PascalCase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "ParameterNamingConvention": "PascalCase"
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int SampleAddNative(int InputValue, int MaxCount);
public static int SampleAdd(int InputValue, int MaxCount)
// NotContains
SampleAddNative(int inputValue, int maxCount)
```

## RuntimeNamespace

### 1. Explanation
**RuntimeNamespace** controls the **RuntimeNamespace** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `string`
- Default value: `string.Empty`
- Example expected value: `"EntryTests.Runtime"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": true,
  "RuntimeNamespace": "EntryTests.Runtime"
}
```

#### Example generated output markers
```csharp
// Contains
using EntryTests.Runtime;
internal static extern Bool8 BoolEvalNative(Bool8 value);
public static bool BoolEval(bool value)
// NotContains
```

## SystemIncludeFolders

### 1. Explanation
**SystemIncludeFolders** controls the **SystemIncludeFolders** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `List<string>`
- Default value: `[]`
- Example expected value: `["sysinclude"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "SystemIncludeFolders": [
    "sysinclude"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern int UseSysNative(int value);
public static int UseSys(int value)
// NotContains
```

## TypeNamingConvention

### 1. Explanation
**TypeNamingConvention** controls the **TypeNamingConvention** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `NamingConvention`
- Default value: `NamingConvention.PascalCase`
- Example expected value: `"CamelCase"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "TypeNamingConvention": "CamelCase"
}
```

#### Example generated output markers
```csharp
// Contains
public partial struct SampleType
internal static extern SampleType MakeSampleNative(int value);
public static SampleType MakeSample(int value)
// NotContains
```

## UseCustomContext

### 1. Explanation
**UseCustomContext** controls the **UseCustomContext** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `false`
- Example expected value: `true`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "FunctionTable",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "UseCustomContext": true
}
```

#### Example generated output markers
```csharp
// Contains
internal static FunctionTable funcTable;
public static void InitApi(INativeContext context)
funcTable = new FunctionTable(context, 1);
funcTable.Load(0, "sample_add");
// NotContains
private static string GetLibraryName()
public static void InitApi()
```

## Usings

### 1. Explanation
**Usings** controls the **Usings** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `List<string>`
- Default value: `[]`
- Example expected value: `["System.Text"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "Usings": [
    "System.Text"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
using System.Text;
internal static extern int SampleAddNative(int a, int b);
// NotContains
using System.IO;
```

## VaryingTypes

### 1. Explanation
**VaryingTypes** controls the **VaryingTypes** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `HashSet<string>`
- Default value: `[]`
- Example expected value: `["ReadOnlySpan<byte>","string","ref string","nint"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "GenerateAdditionalOverloads": true,
  "VaryingTypes": [
    "nint"
  ]
}
```

#### Example generated output markers
```csharp
// Contains
internal static extern void SetNameNative(byte* name);
public static void SetName(ReadOnlySpan<byte> name)
public static void SetName(string name)
// NotContains
```

## WrapPointersAsHandle

### 1. Explanation
**WrapPointersAsHandle** controls the **WrapPointersAsHandle** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `bool`
- Default value: `false`
- Example expected value: `true`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "Namespace": "EntryTests.Generated",
  "ApiName": "EntryApi",
  "LibName": "entry-lib",
  "MergeGeneratedFilesToSingleFile": true,
  "ImportType": "DllImport",
  "EnableExperimentalOptions": true,
  "GenerateExtensions": false,
  "GenerateRuntimeSource": false,
  "WrapPointersAsHandle": true
}
```

#### Example generated output markers
```csharp
// Contains
public unsafe struct SampleTypePtr : IEquatable<SampleTypePtr>
internal static extern int SampleTakePtrNative(SampleType* value);
public static int SampleTakePtr(SampleTypePtr value)
public static int SampleTakePtr(ref SampleType value)
// NotContains
public static int SampleTakePtr(SampleType* value)
```

