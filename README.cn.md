<h1 align="center">BindGen-CS</h1>
<p align="center">从 C/C++ 库，生成清晰的 C# 绑定。</p>
<p align="center"><a href="README.md">English</a> · <a href="#快速开始">快速开始</a> · <a href="#在-c-中调用生成器">C# 接口</a> · <a href="docs/README.cn.md">完整文档</a></p>

**BGCS 将 C/C++ 头文件转换为 C# 绑定。** 可以从终端调用，也可以在自己的 C# 工具中调用；输出只有源码、清单与诊断。

```mermaid
flowchart LR
    H["头文件 + 目标 + 语义规则"] --> G["BGCS：解析、分析、生成"]
    G --> S["C# + 必要桥接源码 + 清单"]
    S --> P["你的项目：编译、链接、包装、部署"]
```

BGCS 不编译或运行你的项目。需要原生桥接时，由你的项目编译生成的桥接源码，并接入原生库。

## 快速开始

需要 **.NET 10 / C# 14**。默认采用 C17 / C++23、实例 API、FunctionTable 导入和分文件输出。

### 1. 准备输入

```bash
dotnet tool install --global BindGen-CS --version 4.1.2
bindgen-cs init native.h --language c
```

假设头文件声明 `int add(int left, int right);`，最小 `bindgen.json` 为：

```json
{
  "schemaVersion": 2,
  "library": { "id": "sample", "nativeName": "sample" },
  "source": { "language": "c", "headers": ["native.h"] },
  "api": { "namespace": "Sample.Generated", "name": "NativeApi", "names": { "add": "Add" } }
}
```

路径相对于声明它的配置文件。默认目标为受支持的本机桌面环境；跨目标生成需要明确提供目标输入。

### 2. 执行生成

```bash
bindgen-cs generate bindgen.json
```

```text
Generated/
├─ generation.report.json
└─ <target>/
   ├─ managed/                 C# 绑定源码
   ├─ native/include/          按需生成的桥接头文件
   ├─ native/src/              按需生成的桥接源码
   └─ metadata/                ABI、原生源码要求与能力清单
```

C++ 使用相同命令，将 `source.language` 设为 `cpp` 即可自动规划 C ABI 桥接。跨边界的按值结构通常转换为类型化指针和输出参数；直接导入必须具备适用的 ABI 调用计划。

### 3. 在应用中使用

把 `managed/` 源码加入自己的 .NET 项目，启用 `AllowUnsafeBlocks`，并引用：

```xml
<PackageReference Include="BGCS.Runtime" Version="4.1.2" />
```

由你的项目构建原生模块后：

```csharp
using System;
using Sample.Generated;

using NativeApi api = NativeApi.Load(args[0]); // 原生模块路径。
Console.WriteLine(api.Add(19, 23));
```

`api.Raw` 提供精确的物理调用入口。有充分的所有权、编码和生命周期依据时，BGCS 才生成对应友好包装，不根据函数名字猜测释放协议。

## 在 C# 中调用生成器

在自己的生成工具中引用 `BGCS`：

```csharp
using System;
using BGCS;

await using BindingHost generator = BindingGenerator.CreateDefault();
GenerationResult result = await generator.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"));

if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

这就是 **BGCS SDK**：可由普通 C# 程序调用的生成库。`BindingHost` 负责协调生成，不是服务器。完整包交付匹配的 Clang 解析资源，首次使用时在本地准备独立解析进程，不下载目标平台 SDK。

## 产品如何选择

| 产品 | 用途 |
| --- | --- |
| `BindGen-CS` | 终端或脚本中的命令行工具。 |
| `BGCS` | 含基础实现的完整生成库。 |
| `BGCS.Core` | 自定义生成扩展所需的中立模型与接口。 |
| `BGCS.Runtime` | 生成代码需要的模块、handle、内存、字符串和回调辅助。 |
| `BGCS.Extension.*` | 按需安装、显式注册的额外目标或执行机制。 |

运行应用只需生成源码、Runtime 支持和原生模块，不需要生成器或 Clang。设置 `runtime.mode = "source"` 可以生成 Runtime 源码，替代程序集引用。

## 通过普通代码扩展

在自己的生成工具项目中实现接口，注册工厂即可：

```csharp
using BGCS;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;

await using BindingHost generator = BindingGenerator.CreateBuilder()
    .RegisterFactory<INativeModuleTransform>(
        new ExtensionDescriptor("sample.rules", ExtensionPort.NativeModuleTransform),
        _ => new SampleRules())
    .Build();
```

`SampleRules` 实现 `INativeModuleTransform`。不要求发布插件、编写 manifest、扫描程序集或接触 Clang AST。目标、runtime、调用 ABI、lowering、API 呈现和源码处理均有相应接口。无法完整声明输入的扩展会禁用受影响的缓存复用。

参阅[扩展接口](docs/extensibility.cn.md)与[示例](examples/README.md)。

## 跨平台如何工作

| 信息 | 来源 |
| --- | --- |
| 生成器运行在哪里？ | 宿主服务选择匹配的解析器资源。 |
| 为哪个原生平台生成？ | 用户的目标配置与目标 provider。 |
| 系统声明来自哪里？ | 用户提供的目标 SDK/sysroot 和头文件搜索目录。 |
| 类型布局是什么？ | frontend 按目标和输入提取事实。 |
| C# 如何调用？ | runtime profile 和调用 ABI provider。 |

Windows 解析器可以分析其他目标的头文件；这不执行目标程序，也不等于证明目标运行能力。私有 SDK 可以通过配置和接口接入，不需要成为 BGCS 依赖。未知 ABI 或不匹配的运行机制会明确失败。

官方按需实现包括 Browser Wasm、NativeAOT、Android、Apple mobile 和 FreeBSD。目标/SDK 解析、源码生成、真实运行分别记录能力，见[支持边界](docs/capabilities.cn.md)。

## 深入使用

| 使用 | 定制 | 理解 |
| --- | --- | --- |
| [快速上手](docs/getting-started.cn.md) | [配置](docs/configuration-guide.cn.md) | [架构](docs/architecture.cn.md) |
| [C# API](docs/api.cn.md) | [原生桥接](docs/native-bridge.cn.md) | [ABI 安全](docs/abi-safety.cn.md) |
| [产品包](docs/packages.cn.md) | [源码处理](docs/source-processing.cn.md) | [Runtime 生命周期](docs/runtime-lifetime.cn.md) |

支持 FunctionTable、DllImport、LibraryImport；实例或显式静态 API；分文件或单文件；本地工作区、检查与差异比较。已支持的 C++ 对象、模板和标准库值可通过 lowering、recipe 或 native shim 适配。

导入方式由选定的 runtime profile 限定。BrowserWasm 扩展支持 DllImport 与 LibraryImport；裁剪后的 Mono Wasm 无法保证任意间接调用签名，因此明确拒绝 FunctionTable。

[完整文档](docs/README.cn.md) · [测试](docs/testing.cn.md) · [贡献](CONTRIBUTING.md)

## 许可证

采用 [MIT](LICENSE)。仍适用的第三方版权与许可保存在 [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md) 及分发资源的许可文件中。
