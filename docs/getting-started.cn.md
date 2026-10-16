# 快速开始

[Wiki](README.cn.md) | [English](getting-started.md)

## 环境要求与平台证据

使用 .NET 10 SDK。生成器、Runtime、工具和默认消费者都以 `net10.0` 为基线，
源码语言基线为 C# 14。完整 SDK 和工具部署匹配的 Clang worker 与原生解析资源，
解析在独立进程中执行。目标需要的系统 SDK、C/C++ 头文件和原生编译器仍由用户准备，
BGCS 不会隐式下载。

解析器资源根据**生成器宿主**选择；头文件、sysroot、布局和原生构建根据**目标**选择。
例如 Windows x64 宿主可以使用 Windows worker，加上 Android NDK，解析 Android ARM64。
解析配置与实际目标、SDK 相容时，可以使用 Clang 解析，再使用 MSVC 或 GNU 编译。
选择 MSVC 时，即使不执行原生构建，也需要已安装的编译器提供初始原生源码视图。
`doctor` 说明 `/Zc:preprocessor`、`/PD` 和 `/C` 前提；生成阶段执行真实预处理验证。
细节见[原生构建](native-build.cn.md)。

目标解析、生成、链接、真实调用是不同的验证阶段。声明平台支持前，请检查
[能力矩阵](capabilities.cn.md) 与对应收据。能够识别目标名称，并不证明所有 ABI、运行时
和导入方式组合已经通过验收。

## 安装或运行仓库中的工具

3.0.0 实际发布之后：

```bash
dotnet tool install --global BindGen-CS --version 3.0.0
bindgen-cs --help
```

后续版本可以通过 `dotnet tool update --global BindGen-CS` 更新；也可以使用仓库本地的
.NET tool manifest 固定版本。在正式发布前，从仓库根目录运行：

```bash
dotnet run --project src/BGCS.Tool -- version
```

下面的 `bindgen-cs` 可以替换成这个源码命令，保留工具参数之前的 `--`。
需要理解的产品只有四个：完整生成 SDK `BGCS`、中立插件契约 `BGCS.Contracts`、
绑定消费者的 `BGCS.Runtime` 和工具 `BindGen-CS`。完整 SDK 或工具直接交付内部实现
程序集；RID 工具分包和解析器 runtime 包属于技术分发细节，见[包分发](packages.cn.md)。

## 创建 C 配置

在公开头文件所在目录执行：

```bash
bindgen-cs init native.h --language c
bindgen-cs doctor bindgen.json
bindgen-cs validate bindgen.json
bindgen-cs inspect bindgen.json --json
bindgen-cs generate bindgen.json
bindgen-cs diff bindgen.json
```

C 与 C++ 都使用 `bindgen.json`。带版本的分组配置示例：

```json
{
  "schemaVersion": 1,
  "library": { "id": "example", "nativeName": "example" },
  "source": { "language": "c", "languageStandard": "c17", "headers": ["native.h"] },
  "api": { "namespace": "Example.Bindings", "name": "NativeApi" },
  "output": { "root": "Generated", "layout": "split" }
}
```

默认选择宿主目标、实例 API、FunctionTable 导入和包形式 Runtime。
`init` 同时写入实际 schema 和 `$schema` 引用。存在歧义的 `.h` 使用
`--language c` 或 `--language cpp`。配置可以放在另外一个目录：

```bash
bindgen-cs init include/native.h --language c --config bindings/bindgen.json
```

每份配置中的相对路径都基于该配置自身目录。CLI `--output` 的相对路径基于调用目录。
本地 `extends` 保留每份基础配置的路径来源，普通列表替换继承列表。未知字段和旧平铺
配置会被拒绝。通过 `init` 创建新配置；3.0 不包含旧配置读取器或迁移包装。

## 输出与原生构建

`generate` 完成解析、分析和完整候选产物生成；发布前验证模型、安全性、源码语法与
实际导入契约。C++ 会自动生成并重新解析 C ABI 桥接。原生编译需要显式构建请求和
明确声明的输入，例如在配置中加入：

```json
{
  "nativeBuild": {
    "mode": "sources",
    "provider": "auto",
    "sources": ["src/native.c"]
  }
}
```

然后执行：

```bash
bindgen-cs build bindgen.json
```

`build` 将警告视为错误验证托管源码，并执行配置声明的原生构建或预编译产物验证。
`nativeBuild.mode = none` 明确跳过原生构建。编译、导出验证和调用分别记录，报告会显示
实际执行了哪些阶段。候选验证失败时，不替换此前发布的完整产物。

宿主目标的输出形态如下，目标名称会解析成具体 ABI：

```text
Generated/
├─ .bgcs-artifacts.json    # 完整所属产物路径与摘要
├─ generation.report.json
└─ windows-x64-msvc/
   ├─ managed/
   │  ├─ Imports/
   │  ├─ Api/
   │  └─ Types/
   ├─ native/
   │  ├─ include/          # 需要 C++ 桥接时生成
   │  ├─ src/              # 需要 C++ 桥接时生成
   │  └─ bin/              # 原生构建成功时生成
   └─ metadata/
      ├─ abi.manifest.json
      └─ verification.json
```

消费者使用根目录 `.bgcs-artifacts.json` ownership manifest 记录的准确文件集合；
`abi.manifest.json` 描述 ABI/投影元数据。选择一个目标的托管源码，不要把
多个不相容目标一起编译。`diff` 不替换正式产物；退出码 0 表示相同，1 表示有变化，
2 表示请求无效或比较失败，包括已经损坏的所属输出。

## 使用生成的绑定

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="BGCS.Runtime" Version="3.0.0" />
</ItemGroup>
```

编译所选目标的 managed 文件并部署原生库。[QuickStart 示例](../examples/QuickStart/README.md)
的调用方式：

```csharp
using QuickStart.Generated;

using NativeApi api = NativeApi.Load("/absolute/path/to/quickstart/native/library");
int answer = api.Raw.Add(19, 23);
```

`Raw` 暴露已经检查的原生 ABI。有足够的字符串、缓冲区和生命周期声明时，生成友好
包装。API 实例各自拥有模块会话，存活的调用和资源持有 lease，直到清理完成。
FunctionTable 在安装可调用上下文前解析全部必需符号。

`Load()` 使用生成 API 所属程序集参与原生 RID 资源定位；自定义 module provider 通过
`FromProvider(...)` 提供。DllImport 和 LibraryImport 使用生成的 `Create()` 工厂和
运行时共享加载器。切换导入方式不会证明尚未支持的按值 ABI。

需要独立 Runtime 源码时选择：

```json
{
  "runtime": { "mode": "source" },
  "output": { "layout": "singleFile", "singleFileName": "Bindings.cs" }
}
```

所需 Runtime 源码闭包采用 API 局部 namespace，可以合入 `managed/Bindings.cs`。
这类消费者不需要 Runtime 包。对一份绑定选择包或生成源码，避免重复实现。

## 聚合头文件与解析选项

SDL3 的 `SDL.h` 等聚合头文件采用分组 source 配置：

```json
{
  "source": {
    "language": "c",
    "headers": ["include/SDL3/SDL.h"],
    "includeDirectories": ["include"],
    "includeTransitivelyReferencedHeaders": true,
    "includeSystemDeclarations": false
  }
}
```

`includeDirectories` 是有序头文件搜索输入。输出与缓存必须位于显式 include/source
根目录以外。入口头文件可以与配置并列，无需把 `.` 声明为 include 目录。
`parseMacros` 和 `parseComments` 控制事实保留。目标 SDK 系统头与 worker 自带的
Clang builtin headers 是不同输入。

## C++ 与高级原生构建

```bash
bindgen-cs init include/widget.hpp --language cpp
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

C++ 默认采用 `c++23`。类、成员和选定模板经过类型化 lowering 转成 C ABI，不能把
原始 C++ 修饰符号当作 C 直接导入。需要明确表示时，配置 `cpp.typeLowerings`、
`cpp.callableLowerings`、`cpp.templateInstantiations`、`cpp.nativeShims` 或中立插件。
原生 shim 可以保留项目专属 C ABI，同时使用统一 worker、构建和发布阶段。
所有权声明仍是用户声明，不会自动成为已验证证据。

也保留只生成原生桥接和根据 manifest 独立构建的入口：

```bash
bindgen-cs bridge bindgen.json
bindgen-cs native-build Generated/<target>/native/metadata/bridge.manifest.json --dry-run --json
bindgen-cs native-build Generated/<target>/native/metadata/bridge.manifest.json
```

provider 支持 `auto`、`clang`、`gnu`、`msvc`、`clang-cl`、`cmake`、`meson`、`msbuild`。
`--compiler`、`--build-tool`、`--auxiliary-tool` 显式选择编译器或构建系统程序。
`--output` 选择最终原生文件，`--package-root` 选择单独的已验证原生资产布局。
这些覆盖参数基于调用目录；manifest 内部路径保留其声明的 manifest/artifact 基准。
编译进程使用参数列表，不调用命令 shell。dry-run 同时展示最终产物位置和实际私有执行计划。

默认验证必需导出，探索性请求可以显式关闭；关闭检查不能满足发布门禁。
musl、GNU、静态 Wasm、Android 和 Apple native reference 都有对应布局与能力证据，
不能只根据一个短 RID 推定完全支持。

## 嵌入式 SDK、插件与 MSBuild

```csharp
using System;
using System.Threading;
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"), CancellationToken.None);
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

需要候选托管/原生构建时调用 `host.BuildAsync(BuildRequest.FromFile(...), ct)`。
`InspectAsync`、`BridgeAsync`、`DiffAsync` 和 workspace 方法也使用同一 Host。
会话在解析前冻结配置和扩展注册。普通插件只引用 `BGCS.Contracts`，处理脱离解析器的
中立事实；manifest 固定可执行文件闭包。影响产物的私有状态和外部输入必须完整提供
指纹，才能复用对应缓存。

[MSBuild 集成](msbuild.cn.md) 随完整 SDK 分发，通过 `BGCSGenerateBindings=true` 启用。
它采用独立 .NET TaskHost，生成到 `obj`，根据 manifest 导入准确的托管和原生 item。
`BGCSBuildNative` 控制是否构建已经声明的原生输入。默认跳过 design-time 和 outer build。
仅安装 SDK 不会启用绑定生成。

## 工作区与定制

```json
{
  "schemaVersion": 1,
  "parallelism": 2,
  "projects": [
    { "id": "core", "configuration": "core/bindgen.json", "dependencies": [] },
    { "id": "extension", "configuration": "extension/bindgen.json", "dependencies": ["core"] }
  ]
}
```

```bash
bindgen-cs workspace validate workspace.json
bindgen-cs workspace generate workspace.json
bindgen-cs workspace build workspace.json
bindgen-cs workspace diff workspace.json
```

项目配置位置基于 workspace 目录，每份绑定保留自己的输入基准。执行前拒绝环路、
输出重叠和受保护输入冲突。失败的前置项目阻止其依赖者继续，独立项目仍可完成。

手写 partial 类型、插件和 recipe 输入放在输出根以外。发布器替换完整 manifest 所属
产物集合，并移除过期生成文件。不要手动修改生成结果让测试通过。
命名使用 `api.names`，语义使用明确契约，自定义变换使用对应的中立扩展端口。

## 排查问题

- **缺少解析器或编译器：** 运行 `doctor bindgen.json`，修正显式 worker、目标 SDK
  或编译器输入。不会静默下载替代实现。
- **没有有效声明：** inspect 原生模型，检查语言、搜索顺序、传递头文件和系统声明选择。
- **不支持按值类型：** 保留指针或原始字节表示，或者声明 native shim。
  类型大小相同不能证明调用约定兼容。
- **缺少友好包装：** 声明实际编码、长度、保留行为、allocator 和释放协议。
  `Create` 等名称不会证明所有权。
- **配置被拒绝：** 运行 `schema bindgen.schema.json` 并采用当前分组配置。
  插件选项属于对应插件 namespace。
- **缓存未命中：** 查看输入和扩展报告。没有完整指纹的扩展禁用受影响的生成缓存，
  不会以旧产物代替正确结果。
- **输出被拒绝：** 选择独立的 BGCS 所属输出根。不会覆盖受保护源码/include/cache，
  也不会覆盖尚未归属 BGCS 的非空目录。

完整发布证据和包安装验收见[测试](testing.md)与[分发](packages.cn.md)。
证据必须来自实际发布 revision。
