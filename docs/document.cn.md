# BindGen-CS 使用方法與完整配置說明

本文件提供：

- 所有主要使用方式（BGCS / COM / Cpp2C / Demo App）
- 配置繼承（`BaseConfig`）行為
- 完整配置欄位解釋（表格）

## 0. 常用配置（先看這裡）

### 0.1 BGCS 常用最小配置

```json
{
  "ApiName": "MyApi",
  "Namespace": "My.Generated",
  "LibName": "mylib",
  "ImportType": "DllImport",
  "EntryFiles": ["headers/api.h"],
  "OutputFilterFiles": ["headers/api.h"]
}
```

### 0.2 BGCS 常用鍵速查

| 鍵 | 作用 | 常見值 |
|---|---|---|
| `ApiName` | 生成主 API 類名 | `MyApi` |
| `Namespace` | 生成命名空間 | `My.Generated` |
| `LibName` | native library 名稱 | `mylib` |
| `ImportType` | 匯入模式 | `DllImport` / `FunctionTable` |
| `GenerateExtensions` | 是否生成 extension | `false`（保守）/`true` |
| `DelegatesAsVoidPointer` | callback 策略 | `false`（typed）/`true` |
| `MergeGeneratedFilesToSingleFile` | 單檔輸出 | `true` / `false` |
| `SingleFileOutputName` | 單檔名稱 | `Bindings.cs` |

### 0.3 Cpp2C 常用最小配置

```json
{
  "NamePrefix": "BGCS_",
  "EntryFiles": ["include/demo.hpp"],
  "OutputFilterFiles": ["include/demo.hpp"]
}
```

### 0.4 Cpp2C 常用鍵速查

| 鍵 | 作用 | 常見值 |
|---|---|---|
| `NamePrefix` | C API 前綴 | `BGCS_` |
| `IncludeFolders` | include 搜尋路徑 | `[]` 或依專案設置 |
| `Defines` | parser 巨集 | 依原生庫需求 |
| `AdditionalArguments` | parser 參數 | 依編譯器/語言標準 |

## 1. 使用方式總覽

## 1.1 BGCS（直接生成 C# 綁定）

```csharp
using BGCS;

var config = CsCodeGeneratorConfig.Load("config.json");
var generator = new CsCodeGenerator(config);
bool ok = generator.Generate("headers/api.h", "Output");
```

## 1.2 BGCS COM 路徑

```csharp
using BGCS;

var config = CsCodeGeneratorConfig.Load("config.com.json");
var generator = new CsComCodeGenerator(config);
bool ok = generator.Generate("headers/com_like.h", "Output");
```

## 1.3 BGCS 多入口檔案

```csharp
using BGCS;

var config = CsCodeGeneratorConfig.Load("config.json");
var generator = new CsCodeGenerator(config);
bool ok = generator.Generate(
    new List<string> { "headers/a.h", "headers/b.h" },
    "Output");
```

## 1.4 BGCS 顯式輸出白名單（strict 輸出控制）

```csharp
using BGCS;

var config = CsCodeGeneratorConfig.Load("config.json");
var generator = new CsCodeGenerator(config);
bool ok = generator.Generate(
    new List<string> { "headers/entry.h" },
    "Output",
    allowedHeaders: new List<string> { "headers/entry.h", "headers/public_only.h" });
```

## 1.5 Cpp2C（C++ -> C bridge）

```csharp
using BGCS.Cpp2C;

var config = Cpp2CGeneratorConfig.Load("config.json");
var generator = new Cpp2CCodeGenerator(config);
generator.Generate("include/demo.hpp", "Output");
```

Cpp2C 預設行為（重要）：

- `Cpp2CCodeGenerator` 會自動確保預設 pipeline 包含：
  - `EnumGenerationStep`
  - `ClassGenerationStep`
- 只有在「你尚未手動註冊任何 step」時才會自動補預設步驟。
- 若你已手動註冊步驟，系統不會自動追加預設步驟。
- 推薦：
  - 一般情境直接使用最短 API（如上）。
  - 只有在客製 pipeline 時才手動加/覆寫 steps。

## 1.6 Demo App 方式

- `demo/BGCS.Demo`:
  - 讀 `config.json`，跑 BGCS / COM 生成流程
- `demo/BGCS.Cpp2C.Demo`:
  - 讀 `config.json`，跑 Cpp2C 生成流程

## 2. BaseConfig（配置繼承）

## 2.1 行為

- 子 config 可宣告：
  - `"BaseConfig": { "Url": "file://config.base.json" }`
- 載入時流程：
  - 先載入 base
  - 再用子 config 覆蓋
- 可透過 `IgnoredProperties` 避免某些欄位繼承。

## 2.2 BaseConfig 欄位

| 欄位 | 型別 | 說明 |
|---|---|---|
| `Url` | `string` | Base config 來源。常用 `file://...`。 |
| `IgnoredProperties` | `HashSet<string>` | 合併時忽略的屬性路徑。 |

## 3. BGCS 配置（`CsCodeGeneratorConfig`）

來源：

- `src/BGCS/CsCodeGeneratorConfig.cs`
- `src/BGCS/CsCodeGeneratorConfig.Mappings.cs`

## 3.1 核心與輸出控制

| 欄位 | 型別 | 預設值 | 說明 |
|---|---|---:|---|
| `BaseConfig` | `BaseConfig?` | `null` | 配置繼承來源。 |
| `Namespace` | `string` | `""` | 生成 C# 命名空間。 |
| `ApiName` | `string` | `""` | 生成 API 主類名稱。 |
| `LibName` | `string` | `""` | Native library 名稱。 |
| `ImportType` | `ImportType` | `FunctionTable` | 匯入方式（`DllImport` / `LibraryImport` / `FunctionTable`）。 |
| `GenerateMetadata` | `bool` | `false` | 是否生成 `NativeName` 等 metadata 屬性。 |
| `GenerateConstants` | `bool` | `true` | 常量生成開關。 |
| `GenerateEnums` | `bool` | `true` | enum 生成開關。 |
| `GenerateExtensions` | `bool` | `true` | extension 生成開關。 |
| `GenerateFunctions` | `bool` | `true` | function 生成開關。 |
| `GenerateHandles` | `bool` | `true` | handle 生成開關。 |
| `GenerateTypes` | `bool` | `true` | type/struct/class 生成開關。 |
| `GenerateDelegates` | `bool` | `true` | delegate 生成開關。 |
| `OneFilePerType` | `bool` | `true` | 是否依型別拆檔。 |
| `MergeGeneratedFilesToSingleFile` | `bool` | `false` | 是否合併成單檔。 |
| `SingleFileOutputName` | `string` | `Bindings.cs` | 單檔模式輸出檔名。 |
| `DeleteSplitFilesAfterMerging` | `bool` | `true` | 合併後是否刪除分檔。 |
| `Usings` | `List<string>` | 由 defaults 初始化 | 附加 using 清單。 |

## 3.2 日誌、解析器與編譯參數

| 欄位 | 型別 | 預設值 | 說明 |
|---|---|---:|---|
| `LogLevel` | `LogSeverity` | `Warning` | Generator 日誌等級。 |
| `CppLogLevel` | `LogSeverity` | `Error` | Cpp parser 診斷等級。 |
| `IncludeFolders` | `List<string>` | `[]` | 解析 include 搜尋路徑。 |
| `SystemIncludeFolders` | `List<string>` | `[]` | 系統 include 搜尋路徑。 |
| `Defines` | `List<string>` | `[]` | 傳給 parser 的 macro defines。 |
| `AdditionalArguments` | `List<string>` | `[]` | 傳給 parser 的額外引數。 |
| `AutoSquashTypedef` | `bool` | `true` | typedef 自動壓平策略。 |

## 3.3 匯入模式與 runtime 互動

| 欄位 | 型別 | 預設值 | 說明 |
|---|---|---:|---|
| `UseCustomContext` | `bool` | `false` | `FunctionTable` 時是否使用自訂 context。 |
| `FunctionTableEntries` | `List<CsFunctionTableEntry>` | `[]` | 預設 function table entry。 |
| `GetLibraryNameFunctionName` | `string` | `GetLibraryName` | FunctionTable 初始化時用的 library name 函式名稱。 |
| `GetLibraryExtensionFunctionName` | `string?` | `null` | FunctionTable 初始化時可選的 extension 函式名稱。 |
| `BoolType` | `BoolType` | `Bool8` | bool 的底層表示（例如 `byte`/`int` 類型策略）。 |

## 3.4 實驗特性與行為開關

| 欄位 | 型別 | 預設值 | 說明 |
|---|---|---:|---|
| `EnableExperimentalOptions` | `bool` | `false` | 啟用實驗選項。 |
| `GenerateSizeOfStructs` | `bool` | `false` | 生成 struct 尺寸相關 helper。 |
| `GenerateConstructorsForStructs` | `bool` | `true` | 生成 struct constructors。 |
| `DelegatesAsVoidPointer` | `bool` | `true` | callback 以 `void*` 或 `delegate*` 表示。 |
| `WrapPointersAsHandle` | `bool` | `false` | 指標包裝策略。 |
| `GeneratePlaceholderComments` | `bool` | `true` | 生成佔位註釋。 |
| `GenerateAdditionalOverloads` | `bool` | `false` | 生成額外 overload。 |

## 3.5 過濾（Allow/Ignored）

| 欄位 | 型別 | 說明 |
|---|---|---|
| `IgnoredParts` | `HashSet<string>` | 忽略特定片段/路徑。 |
| `Keywords` | `HashSet<string>` | 關鍵字集。 |
| `IgnoredFunctions` / `AllowedFunctions` | `HashSet<string>` | function 黑白名單。 |
| `IgnoredExtensions` / `AllowedExtensions` | `HashSet<string>` | extension 黑白名單。 |
| `IgnoredTypes` / `AllowedTypes` | `HashSet<string>` | type 黑白名單。 |
| `IgnoredEnums` / `AllowedEnums` | `HashSet<string>` | enum 黑白名單。 |
| `IgnoredTypedefs` / `AllowedTypedefs` | `HashSet<string>` | typedef 黑白名單。 |
| `IgnoredDelegates` / `AllowedDelegates` | `HashSet<string>` | delegate 黑白名單。 |
| `IgnoredConstants` / `AllowedConstants` | `HashSet<string>` | constant 黑白名單。 |

## 3.6 命名慣例（NamingConvention）

| 欄位 | 預設值 | 說明 |
|---|---|---|
| `ConstantNamingConvention` | `Unknown` | 常量命名。 |
| `EnumNamingConvention` | `PascalCase` | enum 命名。 |
| `EnumItemNamingConvention` | `PascalCase` | enum item 命名。 |
| `ExtensionNamingConvention` | `PascalCase` | extension 方法命名。 |
| `FunctionNamingConvention` | `PascalCase` | function 命名。 |
| `HandleNamingConvention` | `PascalCase` | handle 命名。 |
| `TypeNamingConvention` | `PascalCase` | type 命名。 |
| `DelegateNamingConvention` | `PascalCase` | delegate 命名。 |
| `ParameterNamingConvention` | `CamelCase` | parameter 命名。 |
| `MemberNamingConvention` | `PascalCase` | member 命名。 |

## 3.7 Known* 命名/語義覆寫表

| 欄位 | 型別 | 說明 |
|---|---|---|
| `KnownConstantNames` | `Dictionary<string,string>` | 已知常量名映射。 |
| `KnownEnumValueNames` | `Dictionary<string,string>` | 已知 enum value 名映射。 |
| `KnownEnumPrefixes` | `Dictionary<string,string>` | enum 前綴規則。 |
| `KnownExtensionPrefixes` | `Dictionary<string,string>` | extension 前綴規則。 |
| `KnownExtensionNames` | `Dictionary<string,string>` | extension 名映射。 |
| `KnownDefaultValueNames` | `Dictionary<string,string>` | 預設值命名映射。 |
| `KnownConstructors` | `Dictionary<string,List<string>>` | 已知 constructor 規則。 |
| `KnownMemberFunctions` | `Dictionary<string,List<string>>` | 已知 member 函式規則。 |
| `VaryingTypes` | `HashSet<string>` | 型別變體集合。 |
| `CustomEnums` | `List<CsEnumMetadata>` | 自訂 enum metadata。 |

## 3.8 Mappings（型別/名稱/語義映射）

| 欄位 | 型別 | 說明 |
|---|---|---|
| `IIDMappings` | `Dictionary<string,string>` | COM IID 名稱映射。 |
| `ConstantMappings` | `List<ConstantMapping>` | 常量映射。 |
| `EnumMappings` | `List<EnumMapping>` | enum 映射。 |
| `FunctionMappings` | `List<FunctionMapping>` | function 映射。 |
| `HandleMappings` | `List<HandleMapping>` | handle 映射。 |
| `ClassMappings` | `List<TypeMapping>` | class/type 映射。 |
| `DelegateMappings` | `List<DelegateMapping>` | delegate 映射。 |
| `ArrayMappings` | `List<ArrayMapping>` | array 映射。 |
| `NameMappings` | `Dictionary<string,string>` | 通用名稱映射。 |
| `TypeMappings` | `Dictionary<string,string>` | type 名稱映射。 |
| `TypedefToEnumMappings` | `Dictionary<string,string?>` | typedef -> enum 覆寫。 |
| `FunctionAliasMappings` | `Dictionary<string,List<FunctionAliasMapping>>` | 函式別名映射。 |

## 3.9 程式碼注入/自定義代理（程式碼設定）

以下欄位通常不是 JSON 直接填值，而是程式中指定：

| 欄位 | 型別 | 說明 |
|---|---|---|
| `HeaderInjector` | `HeaderInjectionDelegate?` | 生成檔頭注入委派。 |
| `CustomEnumItemMapper` | `CustomEnumItemMapperDelegate?` | enum item 自定義映射。 |

## 4. Cpp2C 配置（`Cpp2CGeneratorConfig`）

來源：

- `src/BGCS.Cpp2C/Cpp2CGeneratorConfig.cs`

## 4.1 欄位總表

| 欄位 | 型別 | 預設值 | 說明 |
|---|---|---:|---|
| `BaseConfig` | `BaseConfig?` | `null` | 配置繼承來源。 |
| `LogLevel` | `LogSeverity` | `Warning` | Cpp2C generator 日誌等級。 |
| `CppLogLevel` | `LogSeverity` | `Error` | parser 診斷等級。 |
| `IncludeFolders` | `List<string>` | `[]` | include 搜尋路徑。 |
| `SystemIncludeFolders` | `List<string>` | `[]` | 系統 include 搜尋路徑。 |
| `Defines` | `List<string>` | `[]` | parser defines。 |
| `AdditionalArguments` | `List<string>` | `[]` | parser 額外引數。 |
| `NamePrefix` | `string` | `""` | 生成 C API 前綴（例如 `BGCS_`）。 |

## 5. App 層配置（Demo Program）

`demo/BGCS.Demo` 與 `demo/BGCS.Cpp2C.Demo` 額外支援：

| 欄位 | 說明 |
|---|---|
| `EntryFiles` | parser 入口檔清單。 |
| `OutputFilterFiles` | strict 輸出白名單。 |
| `UseComGenerator` | 只在 `BGCS.Demo` 使用，切換 `CsComCodeGenerator`。 |

`OutputFilterFiles` 行為：

- `null`（未提供）: 預設使用 `EntryFiles`
- `[]`（明確空陣列）: 不輸出任何 binding
- 非空陣列: 只輸出該清單來源宣告

## 6. 輸出形態說明

## 6.1 BGCS 輸出

- 預設分檔（Constants/Types/Functions/...）
- 可選單檔合併（`MergeGeneratedFilesToSingleFile`）

## 6.2 Cpp2C 輸出

- 固定多檔結構：
  - `Output/include/*`
  - `Output/src/*`
- 不提供單一檔案輸出模式

## 7. 常見配置策略

## 7.1 最小可用 BGCS

```json
{
  "ApiName": "MyApi",
  "Namespace": "My.Generated",
  "LibName": "mylib",
  "ImportType": "DllImport"
}
```

## 7.2 Base + 子配置分層

`config.base.json` 放共用：

- `ImportType`
- `Generate*` 系列
- `NamingConvention`
- include/defines

子配置只放差異：

- `ApiName`
- `Namespace`
- `EntryFiles`
- `OutputFilterFiles`

## 7.3 C++ 複雜 API 推薦流程

1. 先用 `BGCS.Cpp2C` 產 C bridge
2. 再對 bridge headers 跑 `BGCS` 生 C# binding

此流程可降低直接對 C++ ABI 綁定的風險。
