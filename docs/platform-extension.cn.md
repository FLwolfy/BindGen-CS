# 平台目标扩展与 Solution 组织

[中文 Wiki](README.cn.md) · [架构说明](architecture.cn.md) · [中立扩展](extensibility.cn.md)

## 生成器宿主与原生目标

宿主是执行 BGCS 和解析 worker 的机器；目标是被解析 header 和生成绑定最终运行的 ABI。
例如生成器在 Windows 运行，仍可以在显式提供正确目标 SDK 的情况下解析另一种目标。
是否能编译、链接、签名和运行对应产物，由可用工具链及实际验收决定。

`BGCS.Contracts.Targets` 提供中立请求、描述与 `INativeTargetProvider`；`BGCS.Platforms`
负责目标、SDK、工具链、托管 runtime 和资产机制的解析。Clang 事实提取在独立 frontend worker 中执行。
`NativeModule`、`CppBridgeModule`、`InteropModule` 和 `ManagedApiModel` 分别保存原生事实、
桥接操作、最终 C ABI 与托管呈现，C# backend 不持有 Clang AST。
`BGCS.Runtime` 独立管理消费者调用与生命周期，不承担生成器或应用平台组合。

目标 ID 是 provider 识别的身份，不是 ABI 证明。platform、architecture、ABI、triple、
字节序及工具链必须一致；相同大小或一个 RID 名称不能证明按值调用安全。

动态/静态模块能力也不决定模块名。生成器保留 `library.nativeName`；消费者负责将原生输入注册或链接为该名称。.NET 10 浏览器的 `NativeFileReference` 文件名决定 P/Invoke 模块名（例如 `api.c` / `api.a` 对应 `api`）。`__Internal` 需要消费者显式注册，不能从静态目标能力自动推断。详见[外部编译](native-metadata.cn.md#外部编译)。

## 例子：解析 iOS 设备描述

当前内置 `AppleNativeTargetProvider` 区分 macOS、iOS 设备和 iOS 模拟器。
下面使用当前公开 API 解析描述，调用方必须传入真实 SDK 目录：

```csharp
using BGCS.Contracts.Targets;
using BGCS.Platforms.Targeting;

internal static class TargetSelection
{
    internal static NativeTargetDescriptor ResolveIosDevice(string sdkRoot)
    {
        var request = new NativeTargetRequest(
            new NativeTargetId("ios-arm64-darwin"),
            new NativeToolchainDescriptor(sysRoot: sdkRoot));
        return new TargetResolver().Resolve(request);
    }
}
```

该调用不解析头文件，也不编译或调用 iOS 产物。设备 triple 与模拟器 triple 不同；
生成与静态资产消费还需要正确 SDK、编译器和 runtime profile。
iOS 仍属于[平台矩阵](../eng/acceptance/platform-matrix.json)中的待认证范围。

## 尚未内置的目标

1. 实现 `INativeTargetProvider.TryResolve`。不属于该 provider 的 ID 返回 `false`；属于它但输入无效时明确失败。
2. 用 `NativeTargetDescriptor` 提供 target/platform/architecture/ABI ID、triple 和工具链描述。
   SDK/sysroot 与 Clang builtin headers 保持各自归属。
3. 通过 Host 的 `AddTargetProvider`，或普通插件的 `ExtensionPort.NativeTargetProvider`
   注册会话服务工厂。未知 ID 返回 `false`；多个 provider 认领相同目标时明确拒绝，不按注册顺序覆盖。
4. 新平台族需显式声明托管 runtime 和动态/静态调用能力；原生编译、链接和部署由使用者的外部构建系统负责。
   注册目标不会自动获得未知平台的内置 RID、托管 runtime 或按值 ABI 分类器。
5. 共用 `NativeModule` → `CppBridgeModule`/`InteropModule` → `ManagedApiModel` 流水线。
   无法表达的 ABI 行为在所属事实、分析、构建或 runtime 边界完善；不能从宿主或大小静默猜测。
6. 用自有 fixture 分别验证解析、生成、托管编译、原生链接、真实目标调用与干净包消费。
   指针宽度、布局、参数/返回、三种 import、回调、ownership、缺失符号和失败清理均须有相应证据。

普通插件只引用 `BGCS.Contracts`。下面把调用方提供的 provider 工厂注册为目标服务；
工厂应在每次调用时返回独立实例，不共享可变会话状态：

```csharp
using System;
using BGCS.Contracts.Plugins;
using BGCS.Contracts.Targets;

public sealed class PlatformTargetsPlugin(Func<INativeTargetProvider> createProvider) : IBindingPlugin
{
    public PluginDescriptor descriptor => new(
        "example.targets", "1.0.0",
        [new("example.targets.custom", ExtensionPort.NativeTargetProvider)]);

    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration)
        => registry.RegisterFactory<INativeTargetProvider>(
            "example.targets.custom", createProvider);
}
```

SDK 通过公开 Host 入口组合该插件，随后在请求或分组配置 `targets[].id` 中选择其目标 ID：

```csharp
using System;
using BGCS;
using BGCS.Contracts.Targets;

internal static class PlatformHost
{
    internal static BindingHost Create(Func<INativeTargetProvider> createProvider)
        => BindingHost.CreateBuilder()
            .AddBuiltIns()
            .AddPluginFactory(() => new PlatformTargetsPlugin(createProvider))
            .Build();
}
```

SDK 也可用 `AddTargetProvider("example.targets.custom", createProvider)` 直接注册。
Host 将扩展 provider 与内置 provider 组合。独立使用
`TargetResolver(IEnumerable<INativeTargetProvider>)` 时，参数是完整集合，不会隐式追加内置项；
需要它们时显式组合 `TargetResolver.CreateBuiltInProviders()`。
CLI、workspace 和 MSBuild 通过 `extensions.plugins` 加载带资源锁定的插件 manifest；
其入口需能由 manifest 加载器独立创建，上面的委托注入示例仅演示 SDK 内组合。

provider 若读取外部 SDK 描述等状态，应声明 `IExternalInputProvider` 输入并按协议提供
`ICacheFingerprintProvider` 指纹；缺少完整指纹不能宣称相应缓存可复用。
服务释放、设置来源和加载闭包要求见[中立扩展](extensibility.cn.md)。

新增目标不要求创建 `BGCS.Web`、消费方专用项目或新的 emitter 编排。
复用边界和明确失败规则让改动归属清楚，不保证新 ABI 无需实现任何新规则。

首发强制范围是 Windows x64、Linux GNU x64、Intel macOS 和 Browser Wasm；
ARM64、musl、Android、iOS、FreeBSD 等按平台矩阵单独认证，跨目标解析不能替代实机调用。
Browser Wasm 使用 wasm32 静态适配器及非阻塞生命周期协议，不能套用桌面的动态加载或阻塞清理。
准确能力与限制见[能力与边界](capabilities.cn.md)和[验收规范](acceptance.cn.md)。

## 当前 Solution 组织

```text
BindGen-CS.slnx
  src
    BGCS.Contracts
    BGCS.Infrastructure
    BGCS.Frontend.Clang
    BGCS.Frontend.Clang.Worker
    BGCS.Platforms
    BGCS.Analysis
    BGCS
    BGCS.Cpp2C
    BGCS.CSharp
    BGCS.Runtime
    BGCS.Tool
  integrations
    BGCS.MSBuild
  tests
    BGCS.*.Tests
    fixtures
      进程、宿主和插件 fixture
```

当前共有十三个生产/集成实现工程：`src` 下十二个，另有 `integrations/BGCS.MSBuild`。
测试和 fixture 独立组织，完整项目列表以 [BindGen-CS.slnx](../BindGen-CS.slnx) 为准。
工程引用边界由[依赖规则](../eng/architecture/project-dependencies.json)检查；`Contracts` 和
`Runtime` 不依赖其他 BGCS 工程，普通 lowering 和 C# backend 不引用 frontend。
示例工程由各自流程构建。源码组织完成不能替代同 revision 的构建、调用及产品安装认证。
