# BGCS 3.0 公开 API

[文档目录](README.cn.md) · [English](api.md) · [配置](configuration-guide.cn.md) · [扩展](extensibility.cn.md)

嵌入式 SDK 使用 `BGCS.BindingHost` 和不可变请求。CLI 和工作区调用同一条 Host 流水线。生成器基线为 .NET 10；数据属性采用 camelCase，类型和方法采用 PascalCase。产品版本、Contracts 版本、模型 revision 和 worker 协议 revision 分别管理，可通过 `bindgen-cs version --json` 查询。

## Host 的组合与生命周期

```csharp
using System;
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
```

Host 表示协调器，不是服务器或网络服务。默认 Host 注册内置实现，包括独立 Clang worker 客户端。组合在构建 Host 时冻结。每个会话拥有独立的服务和插件实例；工厂不能把同一个可变对象交给多个无关会话。

需要明确组合时：

```csharp
await using BindingHost host = BindingHost.CreateBuilder()
    .AddBuiltIns()
    .WithWorkerAssetRoot("/absolute/path/to/deployed/worker")
    .WithParseTimeout(TimeSpan.FromSeconds(300))
    .AddPlugin<MyPlugin>()
    .Build();
```

`MyPlugin` 实现 `BGCS.Contracts.Plugins.IBindingPlugin`，普通插件只引用 Contracts。SDK 已部署 worker 时无需手工指定资源根。解析器资源根据运行生成器的**宿主**选择，而不是根据绑定的原生目标选择。

`DisposeAsync()` 拒绝新会话、取消进行中的工作，并等待会话服务释放。每个请求的取消令牌仍独立生效。Host 管理生成器生命周期；生成出的库上下文管理原生调用生命周期。

## 捕获配置与生成

```csharp
GenerationRequest request = await GenerationRequest.FromFileAsync(
    "bindings/bindgen.json", cancellationToken: cancellationToken);
GenerationResult generated = await host.GenerateAsync(request, cancellationToken);
if (!generated.success)
    throw new InvalidOperationException(generated.GetDiagnosticSummary());
```

`FromFileAsync(path, profile: null, cancellationToken: token)` 读取分组且带版本的配置，验证并捕获输入闭包。每份声明文档拥有自己的相对路径基准；捕获请求后，进程工作目录改变不会改变请求含义。
同步入口 `FromFile` 接受相同的可选取消参数和配置加载器。将同一 token 同时传给配置读取与生成，取消可以覆盖配置组合和后续生成阶段。

配置组合只读取本地文件，拒绝 URL 基础配置和网络配置。每个本地 base 保留自己的
路径基准与内容身份。可选 `configurationLoader` 提供本地加载行为，不启用传输。
见[配置组合指南](configuration-guide.cn.md)。

SDK 直接构造配置时使用 `new GenerationRequest(configuration, baseDirectory)`。构造器验证并防御性冻结输入。不可变覆盖保留原始配置来源：

```csharp
GenerationRequest alternative = request.WithConfiguration(
    request.configuration with
    {
        output = request.configuration.output with { root = "Generated/Alternative" }
    });
```

配置分组包括 `library`、`source`、`targets`、`api`、`semantics`、`cpp`、`runtime`、`output`、`extensions`、`cache` 和 `execution`，并支持 `extends` 与命名 profile。C/C++ 共享请求入口。完整约束和默认值见[生成的配置参考](generated/config-reference.cn.md)。

`GenerateAsync` 解析并验证脱离解析器生命周期的原生事实；C++ 还规划桥接、生成并重新解析 C header、核对桥接契约。语义和 ABI 分析、托管投影、源码生成、处理器、最终源码契约核对通过后，才发布完整候选集合。生成成功本身不证明原生链接或真实调用已经通过。

## 生成结果

`GenerationResult` 的 `success`、`published`、`outputRoot`、`artifacts`、`cacheStages`、`targetModels` 和 `diagnostics` 描述生成阶段。输出集合按 owner 与 hash 验证并整体发布。消费者编译、原生编译/链接和实际调用由外部构建与验收负责；生成成功不能替代这些证据。

## 分析、检查、比较与只生成桥接

| 方法 | 结果与用途 |
| --- | --- |
| `AnalyzeAsync(AnalysisRequest, token)` | `AnalysisResult`：原生模块、解析后的具体目标、解析输入与参数文件快照、诊断。 |
| `InspectAsync(GenerationRequest, token)` | `InspectionResult`：各目标的原生、可选桥接、interop 和托管模型，不替换正式输出。 |
| `DiffAsync(GenerationRequest, token)` | `GenerationDiffResult`：Added/Removed/Changed 路径与前后摘要。 |
| `BridgeAsync(GenerationRequest, token)` | `GenerationResult`：原生桥接路径，不要求本来有效的原生导出同时具备托管投影。 |

```csharp
using BGCS.Contracts.Inputs;

AnalysisResult analysis = await host.AnalyzeAsync(
    AnalysisRequest.FromFiles(["include/native.h"], SourceLanguage.C),
    cancellationToken);
GenerationDiffResult difference = await host.DiffAsync(request, cancellationToken);
```

Analysis 请求支持明确目标和工具链、语言标准、头文件搜索输入、宏、参数、前后置源码及解析选项。结果模型不保存 Clang 句柄，分析成功不等于原生构建成功。

检查结果的每个目标还通过 `resolvedTarget` 保留实际锁定的 compiler/SDK descriptor。环境检查复用 Host 已解析事实，不根据目标 ID 文本重新猜工具链。

Diff 在发布锁保护下验证旧输出所有权和完整内容；无所有权、不完整或被修改的输出会失败。正式输出不改变。`hasChanges` 描述逻辑产物差异，不把运行时缓存决策当作 API 改动。

## 工作区

```csharp
BindingWorkspace workspace = new WorkspaceLoader().Load("workspace.json");
WorkspaceResult generated = await host.GenerateWorkspaceAsync(workspace, cancellationToken);
WorkspaceDiffResult compared = await host.DiffWorkspaceAsync(workspace, cancellationToken);
```

每个项目保留独立配置目录、profile、目标选择和输入身份。图构造先检查唯一 ID、依赖、环路和输出/输入冲突；默认并发度为二。结果按声明顺序保留 Completed、Failed 或 Skipped 状态。依赖失败阻止后继执行，独立分支仍保留各自结果。只有全部项目成功，工作区整体才成功。执行不改变进程当前目录。

## 扩展契约

Builder 支持 frontend、managed projection、backend 工厂，以及目标 provider、native/interop transform、C++ type/callable lowering、processor 和 validator 注册。

`AddPlugin<T>()`、`AddPluginFactory(...)` 和 `AddPluginManifest(...)` 组合中立插件。描述声明 Contracts 兼容范围、精确服务 ID、端口、优先级和配置 schema。发现实例与会话实例生命周期独立。重复 ID 或激活时变化的描述会失败；配置独立复制并验证。

`BGCS.Contracts.Plugins.ICacheFingerprintProvider` 捕获影响产物的私有状态和外部输入。缺少完整指纹的扩展仍可运行，但受影响缓存关闭。候选 processor 在可用的 emission 缓存命中后仍运行，然后执行最终源码契约验证和发布。通过受控候选产物接口读写，不直接改正式目录。见[扩展文档](extensibility.cn.md)和[中立示例](../examples/LoweringPlugin/README.md)。

```text
NativeModule → CppBridgeModule（仅 C++）→ InteropModule
→ ManagedApiModel → backend emission plan → 候选产物
```

C# emission plan 属于具体 backend。中立模型不保存 frontend AST、服务实例或物理输出目录。

## 所有权与 worker 资源

`BindingArtifactManifest.Read(outputRoot, libraryId, token)` 在锁内验证完整所有权集合后返回精确描述。集成使用确切 manifest 条目，而不依赖目录 glob。

`host.ClearOwnedOutputAsync(request, token)` 清除验证过的 BGCS 输出，保护输入、缓存和插件资源，保留空的有效所有权标记供后续重建。错误所有者、未登记文件或篡改内容会失败。

`WorkerAssetDeployment.Deploy(sourceRoot, manifestPath, destinationRoot, token)` 部署锁定宿主解析器闭包；`Clear(...)` 保留再次部署所需的有效所有权。SDK 从还原内容进行首次本地准备并使用这些所属规则，不需要 BGCS build targets 或手工组装解析器。进程隔离不是运行不可信插件的安全沙箱。

## 生成的消费 API 与 Runtime

### 值类型载体上的成员

`api.memberFunctions[].kind = "value"` 把明确选择的 C 操作放到已有生成记录上。
receiver 就是调用者的原始值，没有 native factory、析构器、隐藏的 context 字段或
全局库选择。需要重命名载体时使用 `api.records`，不设置 `objectName`。

例如 `int value_add(NativeValue* self, int delta)` 同时声明成员呈现和原生借用契约：

```json
{
  "api": {
    "memberFunctions": [
      { "function": "value_add", "nativeType": "NativeValue", "receiver": "self", "kind": "value", "name": "Add" }
    ]
  },
  "semantics": {
    "valueParameters": [
      { "function": "value_add", "parameter": "self", "direction": "inputOutput", "lifetime": "call", "retainsPointer": false, "allowsNull": false }
    ]
  }
}
```

```csharp
NativeValue value = default;
int result = value.Add(api, 2); // 转发到 api.Add(ref value, 2)
```

只读输入 receiver 会生成 `readonly` 成员，通过 `in this` 转发。context 操作在完整调用
期间固定原始存储并检查 native 对齐，包括与之重叠的其他 `in`/`ref` 值和 Span。
同一个值可以使用多个独立绑定 context；已释放的 context 会拒绝新调用。显式静态
API 模式下，成员接受具体的 `ApiContext`，已有的 `Api.Add(ref value, ...)` 静态操作
仍然可用。不会改变字段、大小、偏移或 raw import。

这条有限投影要求完整、可表达的记录存储，明确不保留指针的 Call 借用，以及受支持
的直接标量/void 返回。仅输出、volatile/未知限定、opaque、柔性数组和过度对齐
receiver 保留 raw API，并说明省略成员投影的原因。status、拥有型返回、callback
和 async 组合需要各自经过检查的投影。值 receiver 不能声明 `objectName` 或
`releaseDomain`。

本地 Windows x64 CoreCLR 测试已实际执行三种 import 与两种 Runtime 模式，包括
native 调用跨越压缩 GC 时仍保持原始地址身份。NativeAOT、浏览器 Wasm、其他系统和
最终发行包的认证分别执行。

### Context 与 Runtime

默认 API 是独立实例，提供精确 raw 调用和符合语义的 friendly wrapper。动态目标提供 `Load(...)`，支持的静态目标提供 `FromStatic()`；自定义 provider 和外部表采用明确 factory。可用 factory 由运行时与导入能力决定。

消费者引用 Runtime 或选择 Runtime 源码输出。Runtime 不需要生成器或解析器。生成上下文持有 `NativeModuleSession`，调用、owned 对象、回调和异步操作保留 lease。关闭后拒绝新公共调用，仍存在的资源保留原生清理能力，最后 lease 释放后才卸载模块。

Friendly 字符串、buffer、所有权和枚举都需要明确且可表达的语义。固定宽度枚举配置改变 friendly 展示，保留 raw 标量 ABI。不支持的按值 ABI 或不明确的所有权形成诊断，不能虚构安全包装。

见[Runtime 生命周期](runtime-lifetime.cn.md)、[ABI 安全](abi-safety.cn.md)、[包](packages.cn.md)和[测试](testing.cn.md)。本机收据仅证明其中列明的目标、运行时、导入和阶段；发布要求全部必需门禁来自同一 revision。

## 显式原生地址

`api.pointerCarriers` 为已有记录指针、opaque 记录指针或具名 `void*` 别名生成地址类型。`pointerDepth` 默认是 `1`；`readOnly: true` 选择第一层 pointee 为 const 的形状。记录名匹配多个限定形状时，应使用 inspect 提供的确切原生指针 ID。生成器不会补造指针事实或存储大小。

可选的 `documentation` 字符串覆盖 carrier 的公开描述，内容作为普通文本转义为 XML 文档，不执行其中的 C# 声明、XML include 或生命周期声明；生成的非拥有型地址提示仍保留。

```json
{
  "api": {
    "pointerCarriers": [
      { "nativeType": "NativeEngine", "name": "EngineAddress" },
      { "nativeType": "NativeWindow", "name": "WindowListAddress", "pointerDepth": 2 }
    ],
    "pointerFunctions": [
      { "function": "get_engine", "returns": "EngineAddress" },
      { "function": "engine_open", "parameters": [{ "parameter": "engine", "carrier": "EngineAddress" }] }
    ]
  }
}
```

地址类型只保存一个 `nint`，提供 `Handle`、空地址检查和地址相等比较，不拥有资源，也不提供 Dispose。raw import 保留原指针 ABI。显式 pointer function 可以组合另行证明的字符串、标量、buffer 和 value 转换；不能替换 owned、module-borrowed 或托管字符串结果，这些结果继续使用原有的 lease API。

完整记录可以提供 `DangerousField_<name>()` 引用和 `DangerousArray_<name>()` 固定数组视图。指针字段按值转换，不把原生指针槽引用重解释为 wrapper 引用。`DangerousGetAddressElement` 按确切的低一层指针 carrier 访问 opaque 等指针表。const 存储使用只读引用或只读 Span；可能暴露 const 存储的可写整记录和 typed pointer 视图不生成。未建模的 volatile 存储不提供便捷视图；opaque 地址没有虚构的解引用能力。

所有 `Dangerous` 视图都要求调用者维持原生分配的生命周期、提供正确范围并同步原生访问。空值、对齐和受检查的偏移运算不能证明这些条件。视图不持有模块 lease；调用者仍须保留适当的上下文或资源 owner。未知生命周期诊断继续服从配置的安全策略。仅生成这类地址展示不代表平台或生命周期协议已经认证。
