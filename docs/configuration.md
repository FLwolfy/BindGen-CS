# BindGen-CS Usage and Full Configuration Reference

This document provides:

- All major usage methods (`BGCS` / `BGCS.Cpp2C`)
- `BaseConfig` composition behavior
- Detailed configuration explanations in table form

## 0. Common Config First (Quick Reference)

### 0.1 BGCS minimal common config

```json
{
  "ApiName": "MyApi",
  "Namespace": "My.Generated",
  "LibName": "mylib",
  "ImportType": "DllImport",
  "EntryFiles": ["headers/api.h"],
  "allowedHeaders": ["headers/api.h"]
}
```

### 0.2 BGCS common keys at a glance

| Key | Purpose | Common values |
|---|---|---|
| `ApiName` | Main generated API type name | `MyApi` |
| `Namespace` | Generated namespace | `My.Generated` |
| `LibName` | Native library name | `mylib` |
| `ImportType` | Import mode | `DllImport` / `FunctionTable` |
| `MergeGeneratedFilesToSingleFile` | Single-file output mode | `true` / `false` |
| `GenerateRuntimeSource` | Generate standalone `Runtime.cs` | `true` / `false` |
| `RuntimeNamespace` | Runtime namespace override | `My.Runtime` / empty (default) |
| `GenerateExtensions` | Generate extension helpers | `false` (strict) / `true` |
| `DelegatesAsVoidPointer` | Callback pointer strategy | `false` (typed) / `true` |

### 0.3 Cpp2C minimal common config

```json
{
  "NamePrefix": "BGCS_",
  "EntryFiles": ["include/demo.hpp"],
  "allowedHeaders": ["include/demo.hpp"]
}
```

### 0.4 Cpp2C common keys at a glance

| Key | Purpose | Common values |
|---|---|---|
| `NamePrefix` | Generated C API prefix | `BGCS_` |
| `IncludeFolders` | Include search paths | `[]` or project-specific |
| `Defines` | Parser macros | Depends on native library |
| `AdditionalArguments` | Parser extra arguments | Compiler/standard specific |

## 1. Usage Overview

### 1.1 BGCS (Generate C# bindings directly)

```csharp
using BGCS;

var config = CsCodeGeneratorConfig.Load("config.json");
var generator = new CsCodeGenerator(config);
bool ok = generator.Generate("headers/api.h", "Output");
```

### 1.2 BGCS with multiple entry files

```csharp
using BGCS;

var config = CsCodeGeneratorConfig.Load("config.json");
var generator = new CsCodeGenerator(config);
bool ok = generator.Generate(
    new List<string> { "headers/a.h", "headers/b.h" },
    "Output");
```

### 1.3 BGCS strict output file filter

```csharp
using BGCS;

var config = CsCodeGeneratorConfig.Load("config.json");
var generator = new CsCodeGenerator(config);
bool ok = generator.Generate(new List<string> { "headers/entry.h" }, "Output");
```

Use `allowedHeaders` in config for strict whitelist control.

### 1.4 Cpp2C (C++ -> C bridge)

```csharp
using BGCS.Cpp2C;

var config = Cpp2CGeneratorConfig.Load("config.json");
var generator = new Cpp2CCodeGenerator(config);
generator.Generate("include/demo.hpp", "Output");
```

Cpp2C default behavior (important):

- `Cpp2CCodeGenerator` now auto-ensures the default pipeline contains:
  - `EnumGenerationStep`
  - `ClassGenerationStep`
- Defaults are auto-applied only when no steps are manually registered.
- Once you manually register steps, defaults are not auto-appended.
- Recommendation:
  - Use the minimal API above for normal scenarios.
  - Manually add/override steps only for custom pipelines.

## 2. BaseConfig Composition

### 2.1 Behavior

- Child config can declare:
  - `"BaseConfig": { "Url": "file://config.base.json" }`
- Load flow:
  - Load base config first
  - Apply child config overrides
- `IgnoredProperties` can exclude inherited paths.

### 2.2 BaseConfig fields

| Field | Type | Description |
|---|---|---|
| `Url` | `string` | Source of base config, usually `file://...` |
| `IgnoredProperties` | `HashSet<string>` | Property paths excluded from inheritance |

## 3. BGCS Config (`CsCodeGeneratorConfig`)

Sources:

- `src/BGCS/CsCodeGeneratorConfig.cs`
- `src/BGCS/CsCodeGeneratorConfig.Mappings.cs`

### 3.1 Core and output control

| Field | Type | Default | Description |
|---|---|---:|---|
| `BaseConfig` | `BaseConfig?` | `null` | Config inheritance source |
| `Namespace` | `string` | `""` | Generated C# namespace |
| `ApiName` | `string` | `""` | Main generated API type name |
| `LibName` | `string` | `""` | Native library name |
| `ImportType` | `ImportType` | `FunctionTable` | Import mode (`DllImport` / `LibraryImport` / `FunctionTable`) |
| `MergeGeneratedFilesToSingleFile` | `bool` | `false` | Merge all generated `.cs` into one |
| `GenerateRuntimeSource` | `bool` | `false` | Generate standalone `Runtime.cs` in output root |
| `RuntimeNamespace` | `string` | `""` | Runtime namespace override (empty => `BGCS.Runtime`) |
| `GenerateMetadata` | `bool` | `false` | Emit metadata attributes |
| `GenerateConstants` | `bool` | `true` | Constant generation switch |
| `GenerateEnums` | `bool` | `true` | Enum generation switch |
| `GenerateExtensions` | `bool` | `true` | Extension generation switch |
| `GenerateFunctions` | `bool` | `true` | Function generation switch |
| `GenerateHandles` | `bool` | `true` | Handle generation switch |
| `GenerateTypes` | `bool` | `true` | Type generation switch |
| `GenerateDelegates` | `bool` | `true` | Delegate generation switch |
| `OneFilePerType` | `bool` | `true` | Per-type split output |
| `Usings` | `List<string>` | defaults | Additional using directives |

### 3.2 Logging, parser, and compiler arguments

| Field | Type | Default | Description |
|---|---|---:|---|
| `LogLevel` | `LogSeverity` | `Warning` | Generator log level |
| `CppLogLevel` | `LogSeverity` | `Error` | C++ parser diagnostics level |
| `IncludeFolders` | `List<string>` | `[]` | Include search paths |
| `SystemIncludeFolders` | `List<string>` | `[]` | System include search paths |
| `Defines` | `List<string>` | `[]` | Parser defines |
| `AdditionalArguments` | `List<string>` | `[]` | Extra parser arguments |
| `AutoSquashTypedef` | `bool` | `true` | Typedef squash strategy |

### 3.3 Import/runtime interaction

| Field | Type | Default | Description |
|---|---|---:|---|
| `UseCustomContext` | `bool` | `false` | Use custom native context in `FunctionTable` mode |
| `FunctionTableEntries` | `List<CsFunctionTableEntry>` | `[]` | Predefined function-table entries |
| `GetLibraryNameFunctionName` | `string` | `GetLibraryName` | Function name used to resolve library name |
| `GetLibraryExtensionFunctionName` | `string?` | `null` | Optional function for extension resolution |
| `BoolType` | `BoolType` | `Bool8` | Bool backing strategy |

### 3.4 Experimental and behavior switches

| Field | Type | Default | Description |
|---|---|---:|---|
| `EnableExperimentalOptions` | `bool` | `false` | Enable experimental options |
| `GenerateSizeOfStructs` | `bool` | `false` | Generate struct-size helpers |
| `GenerateConstructorsForStructs` | `bool` | `true` | Generate struct constructors |
| `DelegatesAsVoidPointer` | `bool` | `true` | Callback representation strategy |
| `WrapPointersAsHandle` | `bool` | `false` | Pointer wrapping strategy |
| `GeneratePlaceholderComments` | `bool` | `true` | Emit placeholder comments |
| `GenerateAdditionalOverloads` | `bool` | `false` | Emit additional overloads |

### 3.5 Filters (Allow/Ignored)

| Field group | Type | Description |
|---|---|---|
| `IgnoredFunctions` / `AllowedFunctions` | `HashSet<string>` | Function blacklist/whitelist |
| `IgnoredExtensions` / `AllowedExtensions` | `HashSet<string>` | Extension blacklist/whitelist |
| `IgnoredTypes` / `AllowedTypes` | `HashSet<string>` | Type blacklist/whitelist |
| `IgnoredEnums` / `AllowedEnums` | `HashSet<string>` | Enum blacklist/whitelist |
| `IgnoredTypedefs` / `AllowedTypedefs` | `HashSet<string>` | Typedef blacklist/whitelist |
| `IgnoredDelegates` / `AllowedDelegates` | `HashSet<string>` | Delegate blacklist/whitelist |
| `IgnoredConstants` / `AllowedConstants` | `HashSet<string>` | Constant blacklist/whitelist |
| `IgnoredParts`, `Keywords` | `HashSet<string>` | Additional global filters |

### 3.6 Naming conventions

| Field | Default |
|---|---|
| `ConstantNamingConvention` | `Unknown` |
| `EnumNamingConvention` | `PascalCase` |
| `EnumItemNamingConvention` | `PascalCase` |
| `ExtensionNamingConvention` | `PascalCase` |
| `FunctionNamingConvention` | `PascalCase` |
| `HandleNamingConvention` | `PascalCase` |
| `TypeNamingConvention` | `PascalCase` |
| `DelegateNamingConvention` | `PascalCase` |
| `ParameterNamingConvention` | `CamelCase` |
| `MemberNamingConvention` | `PascalCase` |

### 3.7 Known* and mapping groups

| Group | Main fields |
|---|---|
| Known naming/semantic tables | `KnownConstantNames`, `KnownEnumValueNames`, `KnownEnumPrefixes`, `KnownExtensionPrefixes`, `KnownExtensionNames`, `KnownDefaultValueNames`, `KnownConstructors`, `KnownMemberFunctions`, `VaryingTypes`, `CustomEnums` |
| Mapping tables | `ConstantMappings`, `EnumMappings`, `FunctionMappings`, `HandleMappings`, `ClassMappings`, `DelegateMappings`, `ArrayMappings`, `NameMappings`, `TypeMappings`, `TypedefToEnumMappings`, `FunctionAliasMappings` |

### 3.8 Programmatic-only delegates

These are usually assigned in code, not plain JSON:

| Field | Type | Description |
|---|---|---|
| `HeaderInjector` | `HeaderInjectionDelegate?` | Generated file header injection |
| `CustomEnumItemMapper` | `CustomEnumItemMapperDelegate?` | Custom enum item mapping |

## 4. Cpp2C Config (`Cpp2CGeneratorConfig`)

Source:

- `src/BGCS.Cpp2C/Cpp2CGeneratorConfig.cs`

| Field | Type | Default | Description |
|---|---|---:|---|
| `BaseConfig` | `BaseConfig?` | `null` | Config inheritance source |
| `LogLevel` | `LogSeverity` | `Warning` | Generator log level |
| `CppLogLevel` | `LogSeverity` | `Error` | Parser diagnostics level |
| `IncludeFolders` | `List<string>` | `[]` | Include search paths |
| `SystemIncludeFolders` | `List<string>` | `[]` | System include search paths |
| `Defines` | `List<string>` | `[]` | Parser defines |
| `AdditionalArguments` | `List<string>` | `[]` | Extra parser arguments |
| `NamePrefix` | `string` | `""` | Prefix for generated C API names |

## 5. Output Forms

### 5.1 BGCS output

- Split files by generation steps/types
- Optional single-file merge (`MergeGeneratedFilesToSingleFile`)
- When `MergeGeneratedFilesToSingleFile = true`, split generated files are automatically removed after merge
- Merged output filename is fixed: `Bindings.cs`
- Runtime output policy:
  - If `GenerateRuntimeSource = true`: runtime is emitted as standalone `Runtime.cs`
  - If `GenerateRuntimeSource = false`: runtime source is not generated
  - Generated bindings use `using {RuntimeNamespace};`
  - `RuntimeNamespace` empty/whitespace => `BGCS.Runtime`

### 5.2 Cpp2C output

- Always multi-file bridge layout:
  - `Output/include/*`
  - `Output/src/*`

No single-file output mode.

## 6. Recommended Configuration Strategy

1. Put shared policies into `config.base.json`:
   - import mode, naming policies, common parser arguments
2. Keep child configs minimal:
   - `ApiName`, `Namespace`, `EntryFiles`, `allowedHeaders`
3. For complex C++ APIs:
   - generate C bridge via `BGCS.Cpp2C` first
   - then generate C# bindings from bridge headers via `BGCS`
