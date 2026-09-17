# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

BindGen-CS 是一个以 Windows 为当前首要验证平台的 C/C++ → C# Binding 工具。它可以为 C ABI 库生成 C# 互操作 API，也可以为不能被 .NET 直接调用的 C++ API 生成 ABI 稳定的 C Bridge。

> **当前状态：** Windows x64 安全与发布 gate 已通过。无法证明安全的 C++ 语义会被拒绝并给出可执行诊断，不会被猜测生成。已验证 corpus 和剩余平台/类型边界见下文与[验收规范](docs/acceptance.cn.md)。

## 项目目标

> 对受支持的 C/C++ ABI 和标准库类型自动生成安全绑定；对缺少 ownership、allocator 或实例化信息的声明进行严格诊断，并只要求最小必要配置。

- 常见库只需要一份配置和一条生成命令。
- 正确支持 Windows MSVC ABI 布局和调用约定。
- C# 与 C Bridge emitter 共用同一个 Binding IR。
- 默认生成接近 `Inno.Native.*` 的清晰 API，不修改生成文件。
- 确定性的 SingleFileOutput。
- 稳定发布 Runtime、Generator、C++ Bridge 和 .NET Tool NuGet 包。
- 提供编译、ABI、运行时、包消费和真实库回归测试。

## 快速开始

```bash
dotnet tool install --global BindGen-CS
bindgen-cs init
bindgen-cs doctor
bindgen-cs validate
bindgen-cs generate
bindgen-cs build
```

`bindgen-cs init` 会创建适合初学者的 `bindgen.json`：

```json
{
  "Namespace": "Native.Bindings",
  "ApiName": "NativeApi",
  "LibName": "native",
  "EntryFiles": ["native.h"],
  "AllowedHeaders": [],
  "IncludeTransitivelyReferencedHeaders": true,
  "OutputPath": "Generated",
  "ParserKind": "C",
  "TargetArchitecture": "X64",
  "ImportType": "DllImport",
  "MergeGeneratedFilesToSingleFile": true,
  "SingleFileOutputName": "Bindings.cs",
  "GenerateRuntimeSource": false
}
```

也可以通过兼容 facade 嵌入到 C# 程序：

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
bool success = generator.GenerateConfigured();

if (!success)
{
    foreach (var diagnostic in generator.Messages)
        Console.Error.WriteLine(diagnostic);
}
```

通过配置生成 C++ Bridge：

```bash
bindgen-cs bridge bridge.json
```

新的应用还可以直接取得分析后的 Binding IR：

```csharp
using BGCS.Facade;
using BGCS.Intermediate;

BindingGenerationResult result = BindingGenerator.Generate("bindgen.json");
BindingModule? module = result.Module;
```

## 最终架构

```text
BGCS
├─ Facade
│  ├─ CsCodeGenerator
│  └─ BindingGenerator
├─ Application
│  └─ BindingGenerationPipeline
├─ Configuration
│  ├─ ConfigLoader
│  ├─ ConfigComposer
│  ├─ ConfigValidator
│  └─ PresetResolver
├─ Analysis
│  ├─ DeclarationGraph
│  ├─ TypeAnalyzer
│  ├─ AbiLayoutAnalyzer
│  ├─ OwnershipAnalyzer
│  └─ OverloadPlanner
├─ Intermediate
│  ├─ BindingModule
│  ├─ BindingType
│  ├─ BindingFunction
│  └─ MarshallingPlan
├─ Emission
│  ├─ CSharpEmitter
│  ├─ CBridgeEmitter
│  ├─ RuntimeEmitter
│  └─ SingleFileComposer
└─ Output
   └─ OutputDirectoryTransaction
```

兼容 facade 已把编排委托给 Application pipeline，C#、Runtime、SingleFile 和 C Bridge 输出都经过明确 emitter 边界。详见[架构说明](docs/architecture.cn.md)。

## 验收目标

稳定主版本只有在每个分类都经过独立测量并达到 **8.5-9.0** 时才算完成。

| 方面 | 目标 | 强制证据 |
| --- | ---: | --- |
| 普通小型 C API | 8.5-9.0 | 生成 C# 可编译，运行时 ABI 测试通过，生成文件零手改 |
| 中大型 C API | 8.5-9.0 | SDL3、miniaudio、cimgui 在预算时间内重新生成 |
| 复杂 C ABI 正确性 | 8.5-9.0 | 强制 Windows x64 布局、pack、union、bitfield、callback、调用约定测试 |
| 普通 C++ class bridge | 8.5-9.0 | 构造析构、方法、重载、继承转换和异常边界测试 |
| 复杂现代 C++ | 8.5-9.0 | 显式模板实例、选定 STL adapter、智能指针和 virtual callback 测试 |
| 生成 API 美观程度 | 8.5-9.0 | Reflection public API 快照通过，生成文件不允许手工 patch |
| 小白易用性 | 8.5-9.0 | init/doctor/validate/generate/build 流程和可执行诊断 |
| 外层架构 | 8.5-9.0 | 强制单向项目依赖和稳定 facade 契约 |
| 内部架构 | 8.5-9.0 | 共享 IR、分析器/emitter 独立测试、消除 God Class |
| NuGet/测试/发布工程化 | 8.5-9.0 | 干净消费、symbols、tool 安装、原生/C# 测试和确定性包 |

详细评分公式、性能预算和 pass/fail 规则以 [docs/acceptance.cn.md](docs/acceptance.cn.md) 为准。`scripts/run-full-test-matrix.sh` 只有在全部强制层通过后才写入 `artifacts/acceptance/report.json`。

| 已测分类 | Windows x64 分数 |
| --- | ---: |
| 普通小型 C API | 9.0 |
| 中大型 C API | 9.0 |
| 复杂 C ABI 正确性 | 9.0 |
| 普通 C++ class bridge | 9.0 |
| 复杂现代 C++ | 9.0 |
| 生成 API 质量 | 9.0 |
| 小白易用性 | 9.0 |
| 外层架构 | 9.0 |
| 内部架构 | 9.0 |
| NuGet/测试/发布 | 9.0 |

当前本地 Windows 真实库 gate 会在不修改生成源码的条件下重新生成 SingleFile，并以 0 C# warning/error 编译：

| 库 | 预算 | 已验证行为 |
| --- | ---: | --- |
| miniaudio split | 60 秒 | 生成和编译 |
| SDL3 umbrella header | 45 秒 | 生成和编译 |
| cimgui | 30 秒 | 生成和编译 |
| cimguizmo | 15 秒 | 生成和编译 |
| bgfx C99 | 30 秒 | 生成和编译 |
| bimg C++ | 15 秒 | C Bridge、clang++、C# 回绑、API snapshot |

Synthetic C 和生成的 C++ Bridge DLL runtime invocation gate 已通过。四个上游 DLL 的直接调用取决于其上游构建系统先产出 DLL，当前不宣称已验证。

## NuGet 包

公开入口包：

- `BGCS`：可嵌入的 C/C++ → C# facade。
- `BGCS.Cpp2C`：C++ → C Bridge。
- `BGCS.Runtime`：生成绑定使用的 Runtime。
- `BindGen-CS`：提供 `bindgen-cs` 命令的 .NET Tool。
- `BGCS.Intermediate`：零依赖共享 Binding IR 和 diagnostics contract。

`BGCS.Core`、`BGCS.Language` 和 `BGCS.CppAst` 是传递实现包。所有发布包使用同一版本，并在发布前执行干净消费者测试。

## 验证命令

```bash
./scripts/run-full-test-matrix.sh
./scripts/test-nuget-packages.sh
```

Windows C++ Bridge 测试使用 `BGCS_CPP2C_CXX`，或者自动发现 `C:\Program Files\LLVM\bin\clang++.exe`。

## Wiki

- [中文 Wiki 入口](docs/README.cn.md)
- [快速开始](docs/getting-started.cn.md)
- [架构说明](docs/architecture.cn.md)
- [配置指南](docs/configuration-guide.cn.md)
- [验收规范](docs/acceptance.cn.md)
- [API 参考](docs/api.md)
- [NuGet 包与导出 API](docs/packages.cn.md)
- [测试说明](docs/testing.md)
- [发布说明](docs/publish.md)

## License

BindGen-CS 使用 MIT License，详见 [LICENSE](LICENSE)。从 CppAst/HexaGen 派生的部分保留其原始版权声明。
