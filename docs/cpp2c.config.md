# Configuration

This is an entry-by-entry specification generated from `BGCS.Cpp2C.Configuration.Tests`.

## Configuration-driven bridge

```json
{
  "EntryFiles": ["include/library.hpp"],
  "AllowedHeaders": ["include/library.hpp"],
  "OutputPath": "GeneratedBridge",
  "TargetCpu": "X86_64",
  "TemplateInstantiations": ["Library::Box<int>"]
}
```

```bash
bindgen-cs bridge bridge.json
```

Paths are resolved relative to `bridge.json`. Loading an existing file does not rewrite it. `TemplateInstantiations` accepts fully qualified class specializations and emits unique C identifiers from their complete C++ names. Primary class templates are not emitted without an explicit specialization. `FunctionTemplateInstantiations` accepts complete declarations such as `int Library::Twice<int>(int)`; BGCS injects a concrete forwarding function so Clang exposes a fully substituted signature instead of the primary `T` cursor.

`Utf8StringTypes` defaults to `std::string` and `std::basic_string<char>`. Input values are lowered to nullable `const char*` and reconstructed as C++ strings. Return values use per-wrapper thread-local C++ storage and return a borrowed UTF-8 pointer that remains valid until the next call to the same wrapper on that thread.

`UniquePtrTypes` defaults to `std::unique_ptr`. A returned `std::unique_ptr<T>` calls `.release()` and transfers a `T*` opaque handle to the caller. A by-value unique pointer parameter reconstructs `std::unique_ptr<T>` from the incoming handle and transfers ownership into C++. Shared IR marks both directions as `BindingOwnership.Transferred`; callers must not reuse a transferred input handle and must destroy an owned returned handle.

`SharedPtrTypes` defaults to `std::shared_ptr`. Each specialization gets a real control-block holder with `Get`, `Clone`, and `Destroy` exports. Returns allocate a holder containing the original shared_ptr; inputs copy from the holder, preserving `use_count`, weak ownership, and `enable_shared_from_this` semantics.

`SpanTypes` defaults to `std::span`. Input spans expand to `T* value, size_t value_count`; returns provide a borrowed element pointer plus `size_t* out_count`. The C++ wrapper reconstructs `std::span<T>`, and the generated C header can be fed back into BGCS to obtain managed `Span<T>` variations.

`VectorTypes` defaults to `std::vector`. Inputs reconstruct a vector from pointer/count iterators. Returns use per-wrapper thread-local vector storage and expose a borrowed pointer/count view valid until the next call on that thread.

`OptionalTypes` defaults to `std::optional`. Input optionals expand to `T value, bool value_has_value`. Optional returns become `bool success` with a trailing output value. Non-blittable alternatives use `T*` input and owned `T** out_value`, copy-construct the native object, and reuse the generated `TDestroy` lifetime protocol.

`VirtualCallbackInterfaces` selects abstract classes that receive a callback-table C struct, user-data pointer, final C++ proxy implementation, and `CreateProxy` factory. Public pure virtual blittable methods are overridden and forwarded to managed function pointers; null callbacks return default values. Reference returns and smart-pointer/span callback signatures are rejected until explicitly lowered.

The default empty `NamePrefix` still uses `BGCS_` for exception-channel exports so `BGCS_GetLastError` cannot collide with Kernel32 `GetLastError` on Windows.

The bridge also emits namespace-qualified free functions into the shared exception boundary. Opaque inherited classes receive `DerivedAsBase` helpers implemented with `static_cast` so multiple-inheritance pointer adjustment is correct. Polymorphic bases additionally receive checked `DerivedFromBase` helpers implemented with `dynamic_cast`.

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

