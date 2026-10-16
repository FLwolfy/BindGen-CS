# BindGen-CS

[English](README.md) | [简体中文](README.cn.md)

**BGCS 为 C / C++ 库生成 C# 绑定。** 提供原生头文件和版本化配置后，统一的 Host
通过独立 Clang worker 解析声明，分析 ABI 与生命周期契约，按需生成 C bridge，
再输出 raw 调用入口和具备依据的友好 C# API。

BGCS 3.0 使用 .NET 10 和 C# 14。默认采用 C17 / C++23、实例 API、FunctionTable
导入、按职责分文件和独立 Runtime 包。新入口是 `BindingHost`，配置采用分组格式；
旧配置读取器与旧生成器 API 不属于这个接口。

## 从头文件开始

3.0.0 发布后：

```bash
dotnet tool install --global BindGen-CS --version 3.0.0
bindgen-cs init include/native.h --language c
bindgen-cs doctor bindgen.json
bindgen-cs validate bindgen.json
bindgen-cs generate bindgen.json
```

C++ 使用同一个配置入口和命令，选择 `--language cpp` 并提供 C++ 头文件。
`generate` 自动生成支持的 native bridge 和 C#。源码使用先构建仓库，再运行工具：

```bash
dotnet build BindGen-CS.slnx -c Release
dotnet run --project src/BGCS.Tool -c Release -- generate examples/QuickStart/bindgen.json
dotnet run --project src/BGCS.Tool -c Release -- build examples/QuickStart/bindgen.json
```

原生构建还需要对应编译器与目标 SDK。解析器携带的 builtin headers 补充目标 SDK
和标准库的编译器支持文件。

## 配置与构建

简单 C 库只需要一个 `bindgen.json`：

```json
{
  "schemaVersion": 1,
  "library": { "id": "example", "nativeName": "example" },
  "source": { "language": "c", "headers": ["include/native.h"] },
  "api": { "namespace": "Example.Bindings", "name": "NativeApi" },
  "nativeBuild": { "mode": "sources", "sources": ["src/native.c"] },
  "output": { "root": "Generated", "layout": "split" }
}
```

配置路径相对于声明该路径的文档解析。`init` 同时写入对应 schema 引用。
`generate` 在 `Generated/<resolved-target>/` 下生成托管源码、按需的 bridge、
manifest 和报告。

`build` 将候选 C# 以 warnings-as-errors 编译，并构建或验证 `nativeBuild` 明确声明的
原生输入，验证必要导出后再发布完整产物集合。验证失败时保留上一套已拥有的输出。
`nativeBuild.mode = "none"` 会在报告中明确记录没有请求原生构建。
实际运行调用是单独的验证阶段。

生成绑定的消费工程使用 `net10.0`，启用 unsafe blocks，通常只引用 `BGCS.Runtime`。
编译所选目标的 `managed` 文件并部署已验证的原生库。QuickStart 将 `bgcs_add` 映射为 `Add`：

```csharp
using QuickStart.Generated;

using NativeApi api = NativeApi.Load("/absolute/path/to/the/built/native/library");
int answer = api.Raw.Add(19, 23);
```

每个 API 实例拥有独立的模块会话和函数表。资源通过 lease 保留清理所需的模块。
`runtime.mode = "source"` 输出所需的 Runtime 源码闭包；`output.layout = "singleFile"`
将托管输出合并为 `Bindings.cs`。完整消费方式、C++ 构造、自定义加载器、Runtime 模式
和构建配置见[快速开始](docs/getting-started.cn.md)。

## 四个产品，共用流水线

| 产品 | 用途 |
| --- | --- |
| `BGCS` | 完整嵌入式 SDK、worker 部署和显式启用的 MSBuild 集成 |
| `BGCS.Contracts` | 中立不可变模型和普通插件契约 |
| `BGCS.Runtime` | 生成绑定的消费支持，不依赖生成器或解析器 |
| `BindGen-CS` | 使用同一套 Host 流水线的命令行工具 |

内部工程作为实现模块随 SDK / 工具交付。用户不需要为 C++、lowering 或原生构建手动
拼装额外功能包。工具 RID 分包与解析器 runtime 包提供六种声明的桌面宿主资源闭包。

```csharp
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"));
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

MSBuild 生成必须明确设置 `BGCSGenerateBindings = true`。编译输入从 `obj` 下已验证的
manifest 精确读取。详见 [MSBuild](docs/msbuild.cn.md)和[包与分发](docs/packages.cn.md)。

## ABI 安全、扩展与平台

FunctionTable 是默认导入方式。目标运行时和 ABI 支持时，可以通过 `api.importMode`
选择 `dllImport` 或 `libraryImport`。三种方式复用原生事实和语义分析。
raw ABI 始终可访问；友好字符串、buffer、拥有型对象和 callback 需要明确的转换与
生命周期契约。大小相同不能证明字段布局、对齐或按值调用兼容。
无法表达的 ABI 会被拒绝，或者要求提供原生 shim。

普通插件引用 `BGCS.Contracts`，通过中立端口扩展目标、模型变换、lowering、
托管投影、产物处理、构建或验证。分组 `cpp` recipe、显式模板选择和原生 shim
表达库特有的 C++ 语义。详见[扩展](docs/extensibility.cn.md)、
[配置指南](docs/configuration-guide.cn.md)和[C++ 实战手册](docs/cpp-extension-cookbook.cn.md)。

运行生成器的宿主、原生目标、工具链和托管运行时各自独立。target provider 提供 ABI
与 SDK 事实；浏览器 Wasm 使用静态原生引用、经过验证的 adapter 和运行能力描述。
新增目标家族不需要复制所有 emitter。原生二进制和实际平台调用仍需独立认证。
请查看[能力表](docs/capabilities.cn.md)与当前收据：能解析目标或生成产物不等于生产支持。

## 命令与验证

| 命令 | 用途 |
| --- | --- |
| `init`、`schema`、`explain` | 创建分组配置、输出 schema、解释诊断 |
| `doctor`、`validate`、`inspect` | 检查环境和配置、查看冻结模型 |
| `generate`、`build` | 生成产物，或验证声明的托管 / 原生构建 |
| `bridge`、`native-build` | 仅请求 native bridge，或根据已验证 manifest 构建 |
| `diff`、`workspace` | 不发布地比较产物，或执行有明确项目依赖的工作区 |
| `supply-chain`、`version` | 检查交付来源与实现身份 |

```bash
dotnet test BindGen-CS.slnx -c Release
python scripts/test-native-aot-bindings.py
python scripts/test-wasm-bindings.py
python scripts/test-wasm-bindings.py --aot
bash scripts/run-full-test-matrix.sh
```

浏览器测试需要 .NET 10 `wasm-tools` workload、Python 和经过接受的浏览器。
发布门禁还覆盖真实库语料、包安装、架构、发布故障、worker 崩溃、生命周期和性能。
完整包安装验收运行真实消费应用，并对比重复打包内容。缺少前提条件会让必需门禁失败。
本地部分检查不能证明所有平台已认证，也不能让版本达到发布条件。

- [文档索引](docs/README.cn.md)
- [测试](docs/testing.md)
- [架构](docs/architecture.cn.md)
- [发布](docs/publish.cn.md)
- [贡献](CONTRIBUTING.md)

## 许可证与解析器资源

BGCS 使用 [MIT License](LICENSE)，衍生组件保留其声明。
[Frontend 资源目录](src/BGCS.Frontend.Clang/Resources/Clang/README.md)记录随包 Clang
builtin headers 的来源、checksum 和 Apache-2.0 WITH LLVM-exception 许可证。
这些资源随匹配的解析 worker 交付，不替代 Windows SDK、libc、C++ 标准库或目标 sysroot。
