# 扩展接口

扩展首先是自己 C# 项目中的一个接口实现。生成契约引用 BGCS.Core；运行中的模块 provider、allocator 和释放/回调 dispatcher 引用 BGCS.Runtime。仅运行绑定的应用不需要生成器。

## 直接注册

```csharp
using BGCS;
using BGCS.Core.Plugins;
using BGCS.Core.Ports;

await using BindingHost generator = BindingGenerator.CreateBuilder()
    .RegisterFactory<INativeModuleTransform>(new("sample.rules", ExtensionPort.NativeModuleTransform), _ => new SampleRules())
    .Build();
```

[完整示例](../examples/InlineExtension/README.md)包含 SampleRules 实现。不要求发布 DLL、manifest、程序集扫描、内部基类或 Clang AST。

| 接口 | 职责 |
| --- | --- |
| IHostServicesProvider | 会话宿主设施，在读取配置前由代码选定。 |
| IFrontend / IIsolatedFrontend | 中立原生事实、准备阶段和依赖输入。 |
| INativeTargetProvider | 目标事实和 SDK/头文件环境。 |
| IManagedRuntimeProvider | 开放 runtime profile 与执行约束。 |
| ICallAbiProvider | 精确正向/反向物理签名的调用分类。 |
| INativeModuleTransform | 原生事实的不可变变换。 |
| INativeTypeLowering / INativeCallableLowering | 类型化 C/C++ 载体、转换与操作。 |
| IInteropTransform | 最终 ABI 语义，修改后重新验证。 |
| IManagedProjection / IManagedApiTransform | 主投影或可组合的托管呈现调整。 |
| IBindingBackend | 源码输出。 |
| ISourceContributor | 原始解析前声明的不可变输入。 |
| IArtifactProcessor | 最终静态检查前处理候选文件。 |
| IGenerationValidator | 静态候选验收，不是消费构建端口。 |

## 选择与生命周期

直接实现、分组与 manifest 使用同一 RegisterFactory 注册机制。descriptor 是 ID、端口和 priority 的唯一权威。重复 ID 失败；ReplaceFactory 在冻结前显式替换相同端口的直接注册服务，不创建旧名别名。

主宿主、frontend 和 backend 必须明确。target、runtime 和 ABI provider 在显式选择后必须唯一适用；不能凭 priority 掩盖竞争声明。可组合变换按 priority 降序、ID 稳定排序。

每个会话拥有自己的服务，不跨会话共享可变对象。生成服务和分组在发布前释放；宿主路径设施持续到最终输入检查与提交结束。分组不能替换读取自身 manifest 时使用的宿主。

## 可选分组与本地加载

IBindingPlugin 组织一组已声明服务。AddPlugin<T> 或 AddPluginFactory 捕获描述并创建独立会话实例；Configure 只注册声明过的端口。本地 CLI manifest 指向已经部署、可校验的程序集闭包；不提供包安装器或市场。

按需官方实现同样显式注册，安装不等于激活。私有 SDK 无需复制进 BGCS；提供目标输入、必要 runtime/ABI 规则和外部输入声明即可。

## 完整缓存输入

通过 ICacheFingerprintProvider 表达所有影响产物的状态，通过 IExternalInputProvider 声明额外文件、目录和缺失输入。代码、依赖、设置、顺序、目标和输入身份均参与缓存。无法完整说明的捕获状态禁用受影响复用；不通过私有反射猜 delegate 闭包。

SourceContributor 提供 bytes、语言、角色与证据；Host 在原始解析前捕获。桥头复核不会重复调用原贡献器。ArtifactProcessor 发生在后面，不能静默改动 raw 导入、ABI 或已计划的公开签名。

## 不能仅靠声明获得的能力

目标名称不会创造新调用 ABI 或 C# runtime。UserAsserted、Declared 不会自动升级为 Verified。不支持的直接签名必须采用正确 pointer/shim 路径或明确失败。扩展是受信任代码，不是安全沙箱。

参阅[架构](architecture.cn.md)、[原生输入](native-source-inputs.cn.md)、[目标示例](../examples/CustomTargetPlugin/README.md)与[lowering](lowering.cn.md)。
