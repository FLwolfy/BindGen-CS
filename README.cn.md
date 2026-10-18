# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

**BGCS 为 C/C++ 库生成 C# 绑定。** 输入是原生头文件和版本化配置，输出是 C#、按需的
C ABI bridge 源码、manifest 和诊断元数据。应用自己的构建系统负责编译、链接、打包和部署。

```text
头文件 + 配置 → BGCS 解析 / 分析 / 生成 → C# + bridge + manifest
                                                   ↓
                                  消费者编译 / 链接 / 打包 / 部署
```

CLI 和普通 C# 类库使用同一流程。`BindingHost` 是一次生成请求的协调器，不是网络服务
或后台守护进程。Clang 解析在独立 worker 内运行。生成器与 `BGCS.Runtime` 分开；后者
为生成后的应用提供调用和生命周期基础设施。

BGCS 3.0 使用 .NET 10 / C# 14，默认 C17/C++23、实例 API、FunctionTable、分文件和
package Runtime。不保留旧生成器 facade、flat 配置读取器或内置消费者/原生构建命令。

## 从头文件生成

3.0.0 发布后：

```bash
dotnet tool install --global BindGen-CS --version 3.0.0
bindgen-cs init include/native.h --language c
bindgen-cs doctor bindgen.json
bindgen-cs validate bindgen.json
bindgen-cs generate bindgen.json
```

源码 checkout 可直接构建并运行工具：

```bash
dotnet build src/BGCS.Tool -c Release
dotnet run --project src/BGCS.Tool -c Release --no-build -- generate examples/QuickStart/bindgen.json
```

C++ 使用相同命令并选择 `--language cpp`，支持的操作自动降为 bridge 源码。
生成器不编译这些源码，也不据此认证下游原生库与声明一致。

```json
{
  "schemaVersion": 1,
  "library": { "id": "example", "nativeName": "example" },
  "source": { "language": "c", "headers": ["include/native.h"] },
  "api": { "namespace": "Example.Bindings", "name": "NativeApi" },
  "output": { "root": "Generated", "layout": "split" }
}
```

相对路径归属声明它的配置，`init` 同时写出 schema。消费工程编译所选目标的托管文件、
启用 unsafe，通常引用 `BGCS.Runtime`。原生工程编译/链接 bridge 时需保持头文件、宏、
packing 和 ABI 一致。见 [QuickStart](examples/QuickStart/README.md)和[快速开始](docs/getting-started.cn.md)。

```csharp
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"));
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

SDK 首次使用时从已还原的包内容准备匹配的宿主 worker，不依赖 BGCS 构建 targets。
可以在自己的构建脚本、应用或工具中调用生成；生成应用的普通构建仍由自己负责。

## 四个产品，十一个实现工程

| 产品 | 用途 |
| --- | --- |
| `BGCS` | 完整嵌入式生成 SDK，包含内置实现和 worker 资源 |
| `BGCS.Contracts` | 中立模型与普通插件契约 |
| `BGCS.Runtime` | 独立的绑定消费支持 |
| `BindGen-CS` | 使用同一生成流程的 CLI |

内部模块随完整 SDK/工具交付，不需要拼装额外 C++ 功能包。
`runtime.mode = "source"` 可改为发射 Runtime 源码；`output.layout = "singleFile"`
将托管输出合并到 `Bindings.cs`。

生成的 API 实例拥有模块会话；调用、拥有型 handle、callback 和 operation 保留清理所需
lease。Raw ABI 可直接使用；string/Span/owned 等友好 API 需要明确语义和生命周期证据。

## 宿主、目标、SDK 与事实

省略目标时检测宿主目标，Linux GNU 与 musl 不混同。显式目标选择另一种 ABI。
provider 映射目标身份和能力，Clang 读取声明并观察目标类型/布局事实。
provider 或目标名称不会替用户提供专有 SDK 头文件。

例如 Windows 可使用 Windows parser worker 和显式提供的 NDK sysroot/头文件路径，
生成 Android ARM64 绑定。Android 编译、链接和设备部署属于消费者。
见[平台扩展](docs/platform-extension.cn.md)。私有插件可描述特殊 SDK/目标，但须提供真实输入、
能力和已有受支持托管 runtime。未知 ABI 分类或 runtime 行为仍需实现或 shim；
这不等于 Switch 或其他专有平台的零配置支持。

Clang 不必是最终原生编译器，但专属头文件分支、布局和调用约定仍须一致。
无法支持的输入会明确诊断，不会通过隐藏的编译器执行路径补丁替换。
大小相同不证明 ABI 兼容；目标解析、生成和实际平台调用是不同结论。

## 扩展与验证

Contracts-only 插件提供目标映射、源码输入、变换、C++ lowering、托管投影、backend、
产物处理和验证。服务归属会话，状态和外部输入需提供缓存指纹；解析器专属高级贡献器
只在 worker 运行。见[扩展](docs/extensibility.cn.md)、[配置](docs/configuration-guide.cn.md)
和 [C++ 手册](docs/cpp-extension-cookbook.cn.md)。

必需 CI 是三个生成器任务（Windows 完整、Linux/macOS smoke）和一个完整产品安装任务。
核心功能基线和原有各宿主性能预算保持必需。AOT/Wasm 应用构建/运行仅为手动、可选的
私有诊断；相应目标事实与源码生成仍在范围内。本地子集不能认证最终发布或所有平台。

- [文档](docs/README.cn.md) · [架构](docs/architecture.cn.md) · [评估](docs/assessment.cn.md)
- [测试](docs/testing.cn.md) · [包](docs/packages.cn.md) · [发布](docs/publish.cn.md)

## 许可证与解析器资源

BGCS 使用 [MIT License](LICENSE)，保留组件维持各自声明。
[解析器资源](src/BGCS.Frontend.Clang/Resources/Clang/README.md)记录 Clang builtin headers
来源及 Apache-2.0 WITH LLVM-exception 许可；它们补充而不替代目标 SDK、libc 或 C++ 标准库。
