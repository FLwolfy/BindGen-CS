# Configuration

This is an entry-by-entry specification generated from `BGCS.Cpp2C.Configuration.Tests`.

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
  "NamePrefix": "Args_",
  "AdditionalArguments": [
    "-DFROM_ADDITIONAL_ARGUMENTS=1"
  ]
}
```

#### Example generated output markers
```cpp
// Contains
typedef struct Args_Counter Args_Counter;
Args_Counter_Add
#define Args_API(type)
// NotContains
```

## BaseConfig

### 1. Explanation
**BaseConfig** is validated through composition behavior, and tests assert the final **NamePrefix** after **BaseConfig** is applied.

### 2. Type, Example, and Default Value
- Type: `BaseConfig?`
- Default value: `null`
- Example expected value: `"Base_"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "BaseConfig": {
    "Url": "file://./base.json"
  }
}
```

#### Example generated output markers
```cpp
// Contains
Base_Counter_Add
BaseMode_A
#define Base_API(type)
// NotContains
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
  "NamePrefix": "CppLog_",
  "CppLogLevel": "Warning"
}
```

#### Example generated output markers
```cpp
// Contains
CppLog_Counter_Add
typedef enum
Mode_A = 1
// NotContains
```

## Defines

### 1. Explanation
**Defines** controls the **Defines** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `List<string>`
- Default value: `[]`
- Example expected value: `["FROM_DEFINES=1"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "NamePrefix": "Def_",
  "Defines": [
    "FROM_DEFINES=1"
  ]
}
```

#### Example generated output markers
```cpp
// Contains
Def_Counter_Add
typedef struct Def_Counter Def_Counter;
// NotContains
```

## IncludeFolders

### 1. Explanation
**IncludeFolders** controls the **IncludeFolders** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `List<string>`
- Default value: `[]`
- Example expected value: `["includes"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "NamePrefix": "Inc_",
  "IncludeFolders": [
    "includes"
  ]
}
```

#### Example generated output markers
```cpp
// Contains
DepMode_A = 1
Inc_Counter_Add
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
  "NamePrefix": "Log_",
  "LogLevel": "Information"
}
```

#### Example generated output markers
```cpp
// Contains
Log_Counter_Add
Mode_A = 1
// NotContains
```

## NamePrefix

### 1. Explanation
**NamePrefix** controls the **NamePrefix** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `string`
- Default value: `string.Empty`
- Example expected value: `"Wrapped_"`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "NamePrefix": "Wrapped_"
}
```

#### Example generated output markers
```cpp
// Contains
typedef struct Wrapped_Counter Wrapped_Counter;
Wrapped_Counter_Add
#define Wrapped_API(type)
// NotContains
```

## SystemIncludeFolders

### 1. Explanation
**SystemIncludeFolders** controls the **SystemIncludeFolders** behavior and is validated by both property snapshots and generated-output checks.

### 2. Type, Example, and Default Value
- Type: `List<string>`
- Default value: `[]`
- Example expected value: `["sysincludes"]`

### 3. Example Config and Generated Output
#### Example config
```json
{
  "NamePrefix": "Sys_",
  "SystemIncludeFolders": [
    "sysincludes"
  ]
}
```

#### Example generated output markers
```cpp
// Contains
typedef struct Sys_Counter Sys_Counter;
Sys_Counter_Add
#define Sys_API(type)
// NotContains
```

