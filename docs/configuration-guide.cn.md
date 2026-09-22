# 配置指南

[Wiki](README.cn.md) | [English](configuration-guide.md) | [具备专门测试的配置条目](config.md)

为了保持源码兼容，JSON 公开模型仍是扁平结构；内部 pipeline 已把 input、target、analysis、marshalling、emission 和 output 职责分开。大型配置应通过 BaseConfig 和 preset 分层，不要复制整份配置。

配置资料的权威顺序是：

1. 当前安装版本运行 `bindgen-cs schema` 得到的完整属性集合；
2. 本指南中的工作流与安全规则；
3. `docs/config.md` 中具备独立 regression test 的行为示例。

`docs/config.md` 不是完整属性枚举，不能替代 schema。

生成供编辑器和 CI 使用的 schema：

```bash
bindgen-cs schema bindgen.schema.json
```

Schema 直接来自当前安装版本的 `CsCodeGeneratorConfig`，包含 enum 名称和可发现的默认值。当前 schema 适合属性发现，但 object-valued mapping 的详细语义仍以本指南和测试为准。

## 先做四个选择

| 决策 | 常用选择 | 何时改变 |
| --- | --- | --- |
| 语言边界 | C header → C# | C++ class/template 改用 C Bridge |
| target | `host-c` | 生成非宿主 ABI 时显式选择 target/triple/sysroot |
| import | `DllImport` | source-generated import 用 `LibraryImport`；运行时加载用 `FunctionTable` |
| runtime | 引用 `BGCS.Runtime` | 需要单文件分发时开启 `GenerateRuntimeSource` |

## 最小配置

```json
{
  "Namespace": "MyCompany.Native.Library",
  "ApiName": "LibraryApi",
  "LibName": "library",
  "Preset": "host-c,c-library",
  "EntryFiles": ["include/library.h"],
  "IncludeFolders": ["include"],
  "ImportType": "DllImport",
  "OutputPath": "Generated"
}
```

这个配置跟随宿主 ABI。可重现的发布配置应使用明确 target preset，或显式填写 platform/architecture/ABI；配置 target 必须与最终 native binary 一致。

## 输入和 Parser

| 属性 | 用途 | 建议 |
| --- | --- | --- |
| `EntryFiles` | 根头文件 | 有稳定 umbrella header 时优先使用 |
| `AllowedHeaders` | 显式输出白名单 | umbrella header 可留空并开启 transitive |
| `IncludeTransitivelyReferencedHeaders` | 包含 entry/include roots 下的用户头 | SDL 风格 API 推荐开启 |
| `IncludeFolders` | 用户 include roots | transitive 模式下允许输出其中声明 |
| `SystemIncludeFolders` | 编译器/system include roots | 通常不输出 |
| `Defines` | 预处理 define | 必须与 native library build 一致 |
| `AdditionalArguments` | 原始 Clang 参数 | 只有没有强类型配置时才使用 |
| `ParserKind` | `C`、`Cpp`、`ObjC` | 与真实 header language 一致 |
| `ParseMacros` | 构建 macro AST | 不需要常量时，大型宏库可关闭 |
| `ParseComments` | 构建文档 AST | 只有不需要文档时才为性能关闭 |
| `ParseSystemIncludes` | 包含系统声明 | 除非主动绑定系统头，否则保持 false |
| `AutoSquashTypedef` | 折叠 typedef chain | 需要保留公开 alias 时关闭 |

## Target

`TargetPlatform`、`TargetArchitecture` 和 `TargetAbi` 共同组成经过验证的 target。`Host` 会解析为当前运行平台/架构；显式 target 覆盖 Windows、Linux、macOS、Android、iOS、FreeBSD 及其有效的 x86/x64/Arm/Arm64 组合。`TargetTriple`、`TargetSysRoot`、`CompilerPath` 提供受控覆盖。Defines 和 native binary 必须与解析后的 target 一致。宿主解析会发现编译器 system include，macOS 还会发现活动 SDK。

“模型支持”不等于“已在该宿主完成验收”。查看[能力矩阵](capabilities.cn.md#target-证据)和当前生成的 acceptance report。

## Import Mode

- `DllImport`：兼容广、诊断简单。
- `LibraryImport`：source-generated import；签名必须满足 source generator 限制。
- `FunctionTable`：显式 native context 和 symbol resolution，与当前 Inno.Native 风格一致。

## Output 与 Runtime

- `GenerateConfigured` 以 config 所在目录解析 `OutputPath`。
- `SingleFileOutputName` 只能是 `.cs` 文件名，禁止路径。
- `GenerateRuntimeSource=false` 需要引用 `BGCS.Runtime`。
- `GenerateRuntimeSource=true` 生成带 guard 的 standalone Runtime。
- 输出是事务性的；解析/生成失败不会删除上一次成功结果。

## Mapping 与 Policy

只为无法安全推断的事实配置 mapping：

- native/managed 命名；
- opaque/unexposed type；
- string encoding 和 ownership；
- 无法通过名称识别的 pointer/count 关系；
- constructor 和 member-style function；
- 显式 template instance 和 C++ adapter。

Mapping 不能掩盖 ABI 不确定性。非平凡 C++ 类型跨边界时必须生成 C Bridge。

## 严格安全诊断

`StrictSafety` 默认是 `true`。`StrictSafetySeverity` 可选 `Warning`（诊断但保持兼容）、`SuppressFriendly`（保留 raw ABI，删除高风险 string/Span/array/delegate overload）或 `Error`（validate/generate/build 在 commit 前失败）。诊断使用 `BGCS-SAFETY-*` code，并给出最小 `MarshallingMappings` 路径。只有外部审计明确负责这些语义时才应设为 `false`。

## Ownership 与 Buffer Marshalling

当 pointer 语法无法表达 ownership 或 buffer 关系时，使用 `MarshallingMappings`：

```json
{
  "MarshallingMappings": {
    "library_create_name": {
      "Return": {
        "Strategy": "String",
        "Ownership": "Owned",
        "Encoding": "Utf8",
        "CleanupFunction": "library_free_name",
        "RequiresCleanup": true,
        "NullTerminated": true
      }
    },
    "library_get_items": {
      "Parameters": {
        "output": {
          "Strategy": "Span",
          "Ownership": "CallerAllocated",
          "LengthParameter": "actual_count",
          "CapacityParameter": "capacity",
          "WrittenCountParameter": "actual_count"
        }
      }
    }
  }
}
```

显式 mapping 会覆盖保守推断，`OverloadPlanner` 不会覆盖这些关系；全部共享 IR emitter 都能通过 `MarshallingPlan` 读取。

## 强类型 C Variadic 函数

没有配置的 `...` 函数会带诊断跳过，因为静默丢掉可变参数会产生 ABI 风险。Windows DllImport 可以配置完成默认参数提升后的固定 variant：

```json
{
  "VariadicFunctionVariants": {
    "native_log": [
      {
        "Suffix": "IntString",
        "ParameterTypes": ["int", "byte*"],
        "ParameterNames": ["value", "text"]
      }
    ]
  }
}
```

类型必须已经体现 C default argument promotion：使用 `double` 而不是 `float`，窄整数使用 `int`。每个 variant 继续调用原始 native EntryPoint。

## BaseConfig

```json
{
  "BaseConfig": {
    "Url": "file://shared.windows-x64.json",
    "IgnoredProperties": ["EntryFiles", "OutputPath"]
  }
}
```

相对 BaseConfig 从引用它的配置目录解析；循环引用会明确失败；读取已有配置不会重写原文件。

## Preset

Preset 可以组合且保持通用：选择一个 target preset（`host-c`、`host-cpp`、`windows-c`、`windows-cpp`、`linux-c`、`linux-cpp`、`macos-c` 或 `macos-cpp`），再按需追加 `c-library`、`function-table`、`opaque-callbacks` 等 API/output policy。库特定事实保留在消费项目配置中，不进入 BindGen-CS core。

```json
{
  "Preset": "host-c,c-library,opaque-callbacks",
  "EntryFiles": ["vendor/SDL/include/SDL3/SDL.h"],
  "IncludeFolders": ["vendor/SDL/include"]
}
```

显式项目配置始终覆盖 preset 默认值，并且与 preset 顺序无关。

## Workspace

Workspace 文件保存多个 config path，适合仓库级自动化：

```bash
bindgen-cs workspace validate native/bindings/workspace.json
bindgen-cs workspace generate native/bindings/workspace.json
bindgen-cs workspace diff native/bindings/workspace.json
```

把 `workspace diff` 放入 CI，可以在不覆盖正式输出的情况下验证全部 checked-in bindings。
