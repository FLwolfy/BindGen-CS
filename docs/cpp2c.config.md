# Tested C++ Bridge Configuration Entries

This catalog is generated from `BGCS.Cpp2C.Configuration.Tests` fixtures and the current `Cpp2CGeneratorConfig` source.
It describes tested behavior; [Configuration Guide](configuration-guide.md) explains composition,
targets, ownership, and failure boundaries. Use the CLI `schema` command for the full binding configuration.

## additionalArguments

- Result property: `additionalArguments`
- Type: `List<string>`
- Source default: `new()`
- Expected fixture value: `["-DFROM_ADDITIONAL_ARGUMENTS=1"]`

### Configuration

```json
{
  "namePrefix": "Args_",
  "additionalArguments": [
    "-DFROM_ADDITIONAL_ARGUMENTS=1"
  ]
}
```

### Generated output assertions

```cpp
// Required markers
typedef struct Args_Counter Args_Counter;
Args_Counter_Add
#define Args_API(type)
// Excluded markers
```

## baseConfig

- Result property: `baseConfig`
- Type: `BaseConfig?`
- Source default: `null`
- Expected fixture value: `"Base_"`

### Configuration

```json
{
  "baseConfig": {
    "url": "file://./base.json"
  }
}
```

### Generated output assertions

```cpp
// Required markers
Base_Counter_Add
BaseMode_A
#define Base_API(type)
// Excluded markers
```

## cppLogLevel

- Result property: `cppLogLevel`
- Type: `LogSeverity`
- Source default: `LogSeverity.Error`
- Expected fixture value: `"Error"`

### Configuration

```json
{
  "namePrefix": "CppLog_",
  "cppLogLevel": "Error"
}
```

### Generated output assertions

```cpp
// Required markers
CppLog_Counter_Add
typedef enum
Mode_A = 1
// Excluded markers
```

## defines

- Result property: `defines`
- Type: `List<string>`
- Source default: `new()`
- Expected fixture value: `["FROM_DEFINES=1"]`

### Configuration

```json
{
  "namePrefix": "Def_",
  "defines": [
    "FROM_DEFINES=1"
  ]
}
```

### Generated output assertions

```cpp
// Required markers
Def_Counter_Add
typedef struct Def_Counter Def_Counter;
// Excluded markers
```

## includeFolders

- Result property: `includeFolders`
- Type: `List<string>`
- Source default: `new()`
- Expected fixture value: `["includes"]`

### Configuration

```json
{
  "namePrefix": "Inc_",
  "includeFolders": [
    "includes"
  ]
}
```

### Generated output assertions

```cpp
// Required markers
DepMode_A = 1
Inc_Counter_Add
// Excluded markers
```

## logLevel

- Result property: `logLevel`
- Type: `LogSeverity`
- Source default: `LogSeverity.Warning`
- Expected fixture value: `"Error"`

### Configuration

```json
{
  "namePrefix": "Log_",
  "logLevel": "Error"
}
```

### Generated output assertions

```cpp
// Required markers
Log_Counter_Add
Mode_A = 1
// Excluded markers
```

## namePrefix

- Result property: `namePrefix`
- Type: `string`
- Source default: `string.Empty`
- Expected fixture value: `"Api_"`

### Configuration

```json
{
  "namePrefix": "Api_"
}
```

### Generated output assertions

```cpp
// Required markers
typedef struct Api_Counter Api_Counter;
Api_Counter_Add
#define Api_API(type)
// Excluded markers
```

## systemIncludeFolders

- Result property: `systemIncludeFolders`
- Type: `List<string>`
- Source default: `new()`
- Expected fixture value: `["sysincludes"]`

### Configuration

```json
{
  "namePrefix": "Sys_",
  "parseSystemIncludes": true,
  "systemIncludeFolders": [
    "sysincludes"
  ]
}
```

### Generated output assertions

```cpp
// Required markers
typedef struct Sys_Counter Sys_Counter;
Sys_Counter_Add
#define Sys_API(type)
// Excluded markers
```
