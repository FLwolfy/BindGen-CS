# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

BindGen-CS 是一个跨平台的 C/C++ → C# binding 工具链。它可以直接为 C API 生成 C# interop，也可以先把 C++ class、模板实例和常见 STL 类型转换成稳定的 C ABI bridge，再生成对应的 C# API。

## 能做什么

- 从 C/C++ header 生成 `DllImport`、`LibraryImport` 或 function-table bindings。
- 生成 raw ABI API，并在安全语义明确时生成常用的 `string`、`Span<T>`、`ref`、`out` 友好重载。
- 处理 struct、union、packing、bitfield、fixed array、typedef、opaque handle、callback 和平台相关基础类型。
- 为 C++ class、构造/析构、成员函数、重载、继承、模板实例、常见 STL container、smart pointer、path 和 chrono 生成 C bridge。
- 自动发现 compiler、target triple、sysroot 和 system include，并生成可复现的 native build manifest。
- 检查真实动态库导出，生成 multi-RID native package 布局，并验证 clean NuGet consumer。
- 用一份 workspace 管理多个 native library；支持确定性 diff、事务性输出和增量缓存。
- 复杂项目语义可通过声明式 lowering、独立 plugin 或项目自己的 C shim 扩展，不需要修改 BGCS core。

BGCS 的目标不是把任意 C++ 源码逐行翻译成 C#，而是把可调用的 native 能力可靠地暴露给 C#。C API 的 ownership、allocator、buffer 或 callback 语义无法证明时，默认会给出诊断、保留 raw ABI、抑制未经证明的 friendly overload；不支持的 C++ lowering 则停止 bridge 生成，直到配置、plugin 或 shim 补充语义。

## Getting Started

需要 .NET SDK 9.0。以下命令直接在源码 checkout 中运行，不依赖尚未发布的公开工具包。C++ bridge 还需要本机 C/C++ compiler。

### 从 C header 生成 C#

在仓库根目录复制运行：

```bash
dotnet run --project src/BGCS.Tool -- init examples/QuickStart/native.h --config examples/QuickStart/bindgen.json
dotnet run --project src/BGCS.Tool -- generate examples/QuickStart/bindgen.json
dotnet run --project src/BGCS.Tool -- build examples/QuickStart/bindgen.json
```

完成后会得到：

```text
examples/QuickStart/Generated/
└─ Bindings.cs
```

`init` 会在示例 header 旁创建可运行配置。`generate` 生成 bindings；`build` 在临时 consumer project 中以 nullable 和 warning-as-error 编译检查。接入自己的库时替换示例 header，并把配置放在它旁边。工具正式发布后可运行 `dotnet tool install --global BindGen-CS`，使用下文较短的 `bindgen-cs` 命令；在此之前可用 `dotnet run --project src/BGCS.Tool --` 替代。

想要单个 C# bindings 文件，在 `bindgen.json` 中设置 `"MergeGeneratedFilesToSingleFile": true`，可用 `"SingleFileOutputName": "Bindings.cs"` 自定义文件名。`init` 的 C-library preset 已默认开启。结果是一份 `Generated/Bindings.cs`，生成文件头会标明 ABI 参考目标。若设置 `GenerateRuntimeSource=true`，还会单独生成 `Runtime.cs`。

首次接入一个库时，推荐执行完整检查：

```bash
bindgen-cs doctor
bindgen-cs validate bindgen.json
bindgen-cs inspect bindgen.json
bindgen-cs build bindgen.json
```

### 从 C++ header 生成 C bridge 和 C#

```bash
bindgen-cs init path/to/library.hpp
bindgen-cs bridge bridge.json
bindgen-cs native-build GeneratedBridge/bridge.manifest.json
```

默认配置会产生：

```text
GeneratedBridge/        # C ABI header、C++ wrapper 和 build manifest
Generated/              # 对应的 C# bindings
```

需要把 native library 放入 NuGet RID 目录时：

```bash
bindgen-cs native-build GeneratedBridge/bridge.manifest.json \
  --package-root artifacts/native-package
```

生成目录是可重建产物，不要直接修改。命名、类型、marshalling、ownership 和函数选择都应写进配置。完整项目结构和常见问题见[快速开始](docs/getting-started.cn.md)。

生成的 C# 源文件只有一条输出路径，不因运行机器而分目录；native binary 仍需按 RID 分发。C/C++ header 与 ABI 可能随 target 变化，例如 enum 底层类型、`long`/`wchar_t`、结构体布局、调用约定和条件编译声明。文件头的 `ABI reference target` 仅记录解析 header 时使用的目标，不代表其他平台已经验证通过。每个目标仍需独立执行 native ABI 与 consumer 测试。详见[目标与输出配置](docs/configuration-guide.cn.md#target)。

## 当前支持状态

图例：已实现并完成当前版本实机验收 ✅　实现或实机验收仍待完成 ⚠️。

| 能力 | 状态 | 说明 |
| --- | :---: | --- |
| C → C# bindings | ✅ | raw ABI 与 friendly API 使用同一 IR-native 生成路径 |
| C++ → C bridge → C# | ✅ | class、继承、模板实例、常见 STL 和 smart pointer 已有 native invocation 测试 |
| 项目扩展 | ✅ | declarative lowering、typed plugin、C shim 和 managed/native artifact |
| multi-RID native package | ✅ | Windows、Linux、macOS x64/arm64 的 `runtimes/<rid>/native/` 布局 |
| SBOM、provenance、API/license/vulnerability gate | ✅ | 本地生成和 release gate 已实现 |
| GitHub OIDC 正式签名 | ⚠️ | workflow 已配置；真实签名只能由授权的 GitHub release run 产生 |

### 平台验收

所有列出的平台都是支持目标。⚠️ 表示当前版本还没有完整的独立实机报告，不表示永久不支持。

| 平台 / 架构 | 状态 | 当前情况 |
| --- | :---: | --- |
| macOS arm64 | ✅ | 完整 `macos-arm64-darwin` 报告通过 |
| Windows x64 | ⚠️ | 等待真实 clang-cl、MSBuild、DLL invocation 和 NuGet consumer 报告 |
| Linux x64 | ⚠️ | 等待同版本完整实机报告和 NuGet consumer 报告 |
| macOS x64 | ⚠️ | ClangSharp 20 未提供上游 Intel native 包；CI 本机构建，仍待完整 Intel 与 multi-RID NuGet consumer 报告 |
| Windows arm64 | ⚠️ | target/RID model 已有；provider 和 runtime 验收待完成 |
| Linux arm64 | ⚠️ | target/RID model 已有；独立完整报告待完成 |
| Android | ⚠️ | NDK/sysroot、package layout 和设备/模拟器验收待完成 |
| iOS | ⚠️ | Xcode SDK、XCFramework layout 和设备/模拟器验收待完成 |
| FreeBSD | ⚠️ | toolchain、package layout 和 runtime 验收待完成 |

详细证据和边界见[能力矩阵](docs/capabilities.cn.md)与[验收规范](docs/acceptance.cn.md)。

## 选择工作流

| 输入或场景 | 使用方式 |
| --- | --- |
| C header / C ABI | `bindgen-cs init native.h`，然后 `generate` 或 `build` |
| C++ class / template / STL | `bindgen-cs init library.hpp`，然后 `bridge` 和 `native-build` |
| 多个 native library | `bindgen-cs workspace validate/generate/diff` |
| 嵌入现有构建工具 | 引用 `BGCS` 或 `BGCS.Cpp2C` |
| 只消费生成代码 | 引用 `BGCS.Runtime` |

## C++ 支持与扩展

已验证范围包括构造/析构、instance/static method、overload、namespace function、异常边界、多继承 pointer adjustment、完整/部分模板特化、显式模板实例、`string`、`vector`、`span`、`array`、`map`、`set`、`optional`、`variant`、`expected`、`filesystem::path`、`chrono`、`unique_ptr`、`shared_ptr` 和配置式 pure-virtual callback proxy。

项目特有类型和行为有三种扩展方式：

1. `TypeLowerings` / `CallableLowerings`：用 JSON 描述稳定转换。
2. typed lowering plugin：需要 AST 判断、target 分支或额外生成文件时使用。
3. `NativeShims`：用项目自己的 C/C++ 建立明确的 C ABI 边界。

完整示例见[C++ 扩展实战手册](docs/cpp-extension-cookbook.cn.md)。它包含可运行的 shim、独立 plugin 项目、callback/async/allocator 推荐模式，以及何时使用 `AllowUnsafe`。

## 常用命令

| 命令 | 用途 |
| --- | --- |
| `init` | 从 header 创建起始配置 |
| `doctor` | 检查 host target、compiler、system include 和 SDK |
| `validate` | 解析和分析 C binding 配置，不写正式输出 |
| `inspect` | 查看分析后的 module 摘要或 JSON |
| `generate` | 事务性生成 C# bindings |
| `build` | 生成并编译检查 C# bindings |
| `diff` | 检查已提交 bindings 是否需要重新生成 |
| `workspace` | 批量处理多个配置 |
| `bridge` | 生成 C++ → C bridge 和可选 C# bindings |
| `native-build` | 编译 bridge、检查导出并可选写入 RID package |
| `schema` | 从当前版本生成严格 JSON Schema |
| `explain` | 解释稳定诊断代码 |
| `supply-chain` | 生成 SPDX SBOM 和 SLSA provenance |

## 安全行为

- ABI 和 lifetime 明确：直接生成并编译验证。
- 缺少 ownership、allocator、buffer length 或 callback lifetime：默认给出 `BGCS-SAFETY-*` 诊断、保留 raw ABI、抑制未经证明的 friendly overload。可设 `StrictSafetySeverity=Error` 拒绝整次生成，或提供 `MarshallingMappings` 明确契约后恢复 friendly API。
- C++ 类型没有可接受的 lowering：拒绝生成，直到加入 recipe、plugin 或 shim。
- `AllowUnsafe` 只表示项目明确接管风险，并保留审计诊断；它不能把错误 ABI 变正确。

## 在构建工具中使用

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
if (!generator.GenerateConfigured())
{
    foreach (var diagnostic in generator.Messages)
        Console.Error.WriteLine(diagnostic);
}
```

需要结构化结果时使用 `BGCS.Facade.BindingGenerator` 和 `BGCS.Intermediate` 中的 Binding IR。

## 测试与发布

```bash
./scripts/run-full-test-matrix.sh
```

完整矩阵包含 managed tests、native ABI/runtime、C++ 语义、真实库、public API、deterministic snapshot、NuGet consumer、license/vulnerability 和性能 gate。报告格式见[验收规范](docs/acceptance.cn.md)。发布流程和 OIDC 条件见[发布说明](docs/publish.cn.md)。

## 包

- `BindGen-CS`：提供 `bindgen-cs` 命令的 .NET Tool。
- `BGCS`：可嵌入的 C/C++ → C# facade。
- `BGCS.Cpp2C`：C++ → C bridge。
- `BGCS.Runtime`：生成 bindings 使用的 runtime。
- `BGCS.Intermediate`：无生成器依赖的 Binding IR 和 diagnostics contract。

## 文档

- [文档入口](docs/README.cn.md)
- [快速开始](docs/getting-started.cn.md)
- [配置指南](docs/configuration-guide.cn.md)
- [能力与边界](docs/capabilities.cn.md)
- [C++ 扩展实战手册](docs/cpp-extension-cookbook.cn.md)
- [诊断指南](docs/diagnostics.cn.md)
- [架构说明](docs/architecture.cn.md)
- [测试与验收](docs/testing.md)
- [发布与 OIDC](docs/publish.cn.md)

## License

BindGen-CS 使用 MIT License，详见 [LICENSE](LICENSE)。从 CppAst/HexaGen 派生的部分保留其原始版权声明。
