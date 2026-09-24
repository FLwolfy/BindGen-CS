# 快速开始

[Wiki](README.cn.md) | [English](getting-started.md)

## 环境要求

- 支持的 x86/x64/Arm/Arm64 target 上的 Windows、Linux 或 macOS。
- .NET SDK 9.0。
- LibClang 由 BGCS 包恢复。
- 发现系统头和编译 C Bridge 需要 C/C++ compiler driver。BindGen-CS 会发现 Clang/GNU driver、Windows LLVM 安装和活动 macOS SDK；也可以通过 `BGCS_CC`、`BGCS_CPP2C_CXX`、`CC`、`CXX` 显式覆盖。

开始前先确认工作流：纯 C ABI 使用 `bindgen.json`；C++ class/template 使用 `bridge.json`。不要直接对没有 C linkage 的 C++ symbol 生成 P/Invoke。

## 运行工具

仓库 [README 一分钟示例](../README.cn.md#getting-started) 使用 `dotnet run --project src/BGCS.Tool --` 和已提交的 `examples/QuickStart/native.h`，不依赖公开工具包。下文的 `bindgen-cs` 命令在源码 checkout 中可替换为该命令。

`BindGen-CS` 工具包正式公开发布后，可以安装：

```bash
dotnet tool install --global BindGen-CS
bindgen-cs --help
```

发布后可用 `dotnet tool update --global BindGen-CS` 升级。仓库内固定版本可使用标准 .NET tool manifest。

## 生成 C Binding

在包含 `native.h` 的目录执行：

```bash
bindgen-cs init native.h
bindgen-cs doctor
bindgen-cs validate bindgen.json
bindgen-cs inspect bindgen.json
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

默认输出是 `Generated/Bindings.cs`。成功路径应当得到：

```text
project/
├─ native.h
├─ bindgen.json
└─ Generated/
   └─ Bindings.cs
```

`bindgen-cs build` 会把 warning 当 error 编译生成源码，但不会编译上游 native library。`bindgen-cs diff` 在不替换正式文件的情况下检查已提交 bindings 是否最新。消费项目安装 Runtime：

```bash
dotnet add package BGCS.Runtime
```

如果启用了 `GenerateRuntimeSource`，应编译生成的 `Runtime.cs`，不要同时引用重复 Runtime 类型。

`init` 会写入相对于生成配置的可移植路径。对于语义不明确的 `.h`，可以显式选择工作流；使用 `--config` 可以把配置放在其他目录：

```bash
bindgen-cs init include/native.h --language c --config bindings/bindgen.json
bindgen-cs init include/library.h --language cpp --config bindings/bridge.json
```

## 使用 Umbrella Header

SDL3 的 `SDL.h` 一类入口需要：

```json
{
  "EntryFiles": ["include/SDL3/SDL.h"],
  "IncludeFolders": ["include"],
  "AllowedHeaders": [],
  "IncludeTransitivelyReferencedHeaders": true
}
```

只有 entry 目录和 IncludeFolders 下的用户声明会进入结果。除非开启 `ParseSystemIncludes`，编译器系统头不会被绑定。

## 选择 C 或 C++

```json
{
  "ParserKind": "C"
}
```

C 库使用 `C`；C++ 声明或需要 C++ extension 的头使用 `Cpp`。没有 C linkage 的 C++ symbol 通常必须经过 `BGCS.Cpp2C`，P/Invoke 不能直接调用 mangled C++ member function。

## 嵌入式 Facade

```csharp
using BGCS;

CsCodeGenerator generator = CsCodeGenerator.Create("bindgen.json");
generator.LogToConsole();
if (!generator.GenerateConfigured())
    throw new InvalidOperationException("Binding generation failed.");
```

最近一次运行可以通过 `LastResult.Module` 取得共享 Binding IR。

## C++ Bridge

从 header 创建推荐配置：

```bash
bindgen-cs init include/library.hpp
```

`.hpp`、`.hh`、`.hxx` 会生成 `bridge.json`。下面是等价的核心结构。

创建 `bridge.json`：

```json
{
  "ConfigVersion": 1,
  "EntryFiles": ["include/library.hpp"],
  "AllowedHeaders": ["include/library.hpp"],
  "OutputPath": "GeneratedBridge",
  "LanguageStandard": "c++23",
  "GenerateBuildManifest": true
}
```

通过统一工具生成：

```bash
bindgen-cs bridge bridge.json
bindgen-cs native-build GeneratedBridge/bridge.manifest.json
```

设置 `GenerateCSharpBindings=true`，并提供 `CSharpNamespace`、`CSharpApiName`、`NativeLibraryName`、`CSharpOutputPath`，即可在这一条命令中同时生成 native C Bridge 和 C# bindings。可选 C# 输出默认采用 `CSharpStrictSafetySeverity=SuppressFriendly`：ownership/lifetime 未证明时保留 raw ABI，但抑制推断的 friendly 方法。项目独立审计这些契约后才能显式选择 `Warning`；`Error` 则在补充配置前拒绝生成。

嵌入式 API：

```csharp
using BGCS.Cpp2C;

Cpp2CGeneratorConfig config = Cpp2CGeneratorConfig.Load("bridge.json");
Cpp2CCodeGenerator generator = new(config);
generator.Generate("include/library.hpp", "GeneratedBridge");
```

把生成的 `src/Classes.cpp`、`include` 和原库 include path 编译成 DLL，再让 BGCS 读取生成的 C 头。自动 native linking 仍取决于原库构建，是独立验收项。

生成的 `bridge.manifest.json` 是交给 native build 自动化的确定性契约。路径尽量相对于 manifest，内容包括 target、C++ standard、generated/original files、include directories、defines、compiler/linker arguments、library search path 和 libraries。`LanguageStandard` 默认 `c++23`；`AdditionalArguments` 中已有的 `-std=` 仍作为显式兼容覆盖。

可以只检查计划、覆盖 compiler，或指定明确 artifact path：

```bash
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --dry-run --json
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --compiler clang++ --output artifacts/libnative.dylib
bindgen-cs native-build GeneratedBridge/bridge.manifest.json --package-root artifacts/native-package
```

`native-build` 不调用 command shell，可选择 `auto`、direct Clang/GNU、clang-cl、CMake、Meson 或 MSBuild。所有 provider 使用同一份 manifest 中的 library search path、link library、linker argument、target triple 和 sysroot（在对应 backend 支持范围内）；构建成功后默认将生成声明与二进制 export table 核对。

`--output` 和 `--package-root` 相对命令运行目录解析；`bridge.manifest.json` *内部*的路径相对 manifest 解析。`--package-root` 只在 binary 文件名、格式和 CPU 架构与 target 匹配后写入 `runtimes/<rid>/native/`，并在 `bgcs.native-assets.json` 记录 target 与 SHA-256。Windows/Linux/macOS x64/arm64 当前映射到 NuGet RID；Android/iOS/FreeBSD 是正式目标，但 package layout 尚未验收，helper 会明确诊断。Pack 完成后可运行 `bindgen-cs supply-chain artifacts/nuget --output artifacts/supply-chain --revision <commit> --timestamp <source-date>`。

不要假定任意 template/STL type 都能自动 lowering。显式实例和内置 lowering 见[能力矩阵](capabilities.cn.md)，项目扩展见[最终 lowering 架构](lowering.cn.md)，拒绝原因见[诊断指南](diagnostics.cn.md)。

需要完整的 recipe、plugin、native shim、ownership、callback 和 async 示例时，继续阅读[C++ 扩展实战手册](cpp-extension-cookbook.cn.md)。

## 多项目 Workspace

当一个仓库包含多个 native library 时，用 workspace 固定生成顺序和配置入口：

```bash
bindgen-cs workspace validate native/bindings/workspace.json
bindgen-cs workspace generate native/bindings/workspace.json
bindgen-cs workspace diff native/bindings/workspace.json
```

CI 通常运行 `workspace diff`，确保配置与 checked-in bindings 一致。

## 不要修改生成文件

自定义逻辑应放在：

- naming/type/function mapping；
- preset 和 policy；
- 输出目录外的 pre/post patch；
- 独立目录中的手写 partial 类型。

成功后 output transaction 会整体替换生成目录，因此直接修改生成文件不会被保留。

## 常见问题

- **没有声明：** 检查 `AllowedHeaders`；umbrella header 应开启 transitive user headers。
- **解析过慢：** 不需要时关闭宏和注释，并把对应头作为性能回归；真实库预算是强制验收项。
- **未知类型：** 添加 `TypeMappings` 或生成 C++ bridge specialization；禁止把非平凡 C++ value type 直接映射成 blittable C# struct。
- **Runtime 重复：** 使用 `BGCS.Runtime` 或生成 `Runtime.cs`，不要无保护地同时使用。
- **找不到原生编译器：** 把 `BGCS_CC` 与 `BGCS_CPP2C_CXX`（或 `CC`/`CXX`）指向对应 compiler driver。
- **需要完整配置属性列表：** 运行 `bindgen-cs schema bindgen.schema.json`；`docs/config.md` 只列出具备专门 entry regression test 的属性。
