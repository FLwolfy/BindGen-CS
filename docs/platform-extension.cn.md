# 宿主、目标与私有 SDK 扩展

[文档](README.cn.md) · [架构](architecture.cn.md) · [接口扩展](extensibility.cn.md)

BGCS 认识平台，是为了正确解析声明和生成调用边界。它不构建目标应用。

## 五种信息分别来自哪里

| 信息 | 来源与责任 |
| --- | --- |
| 生成器宿主 | `IHostServicesProvider` 提供路径与执行事实；解析资源匹配当前进程。 |
| 原生目标 | 用户选择与 `INativeTargetProvider` 提供 CPU、ABI、triple 和源码约定。 |
| SDK 与系统头 | 用户安装并明确提供的 sysroot、头目录、宏及解析参数；仅本机受支持默认环境允许只读发现。 |
| 类型布局 | frontend 按上述输入提取真实 size、alignment、字段与类型事实。 |
| C# 如何调用 | `IManagedRuntimeProvider` 和 `ICallAbiProvider` 对精确签名提供适用计划。 |

Clang builtin headers 是解析器自己的支持文件，不代替操作系统 SDK、libc 或 C++ 标准库。
显式 SDK 具有权威性，不能用宿主的另一套头文件悄悄补齐。GNU 与 musl 独立；未知 libc 不按 GNU 处理。

## Windows 为 Android 生成

```text
Windows 进程 → 使用匹配的 Windows parser/worker
Android ARM64 目标 + 用户本地 NDK → 按 Android 声明、宏与布局解析
生成 C# 和必要桥接源码 → 消费项目编译、链接、部署与验证
```

`BGCS.Extension.Android` 提供 NDK 输入与目标规则，需要显式注册。该扩展本身不提供任意
Android 托管执行模型；生成 managed 调用仍需适用的 runtime 与 ABI provider。仅解析目标时，
不必把目标名称伪装成桌面 CoreCLR。C++ 也需要相匹配的 NDK libc++ 头文件环境。

BGCS 不运行目标程序、不安装 NDK、不生成 APK。跨目标解析和源码生成成功也不等于设备调用已认证。

## 直接实现接口

目标实现只需引用 `BGCS.Core`，实现 `INativeTargetProvider.TryResolve`，然后注册会话工厂：

```csharp
await using BindingHost generator = BindingGenerator.CreateBuilder()
    .RegisterFactory<INativeTargetProvider>(
        new ExtensionDescriptor("sample.target", ExtensionPort.NativeTargetProvider),
        _ => new SampleTargetProvider())
    .Build();
```

未知 ID 返回 false。重复独占匹配失败；不会用优先级掩盖冲突。宿主 provider 必须在读取配置前通过代码选择，
不能从之后加载的配置 manifest 替换。普通 SDK 使用者不必发布 NuGet、创建独立 DLL 或编写 manifest。

需要 CLI 加载时，可把注册组织成 `IBindingPlugin`，使用显式本地 manifest。
完整例子见 [CustomTargetPlugin](../examples/CustomTargetPlugin/README.md)；
该示例复用真实桌面 ABI，证明扩展流程，不代表任何专有主机 SDK 已验证。

## 私有 SDK 与新运行机制

SDK 可以留在用户自己的私有工程。目标 provider 声明实际读取的版本文件、规则文件和头目录，
同时提供实现、设置及外部输入指纹。只记录 SDK 根路径不足以建立缓存身份；无法完整说明输入时禁用受影响缓存。

新 SDK 若采用已有目标与调用机制，通常只需配置或目标实现。新的类型转换使用
`INativeTypeLowering` / `INativeCallableLowering`；新的托管执行能力使用 runtime/ABI 接口；
原生符号取得由独立 Runtime 的 `INativeModuleProvider` 或外部函数表实现。

注册名称、声明能力或相同类型大小，均不能证明按值调用正确。无法建立可靠调用计划时拒绝，
或通过明确的 pointer / C shim 转换。BGCS 不创造 C# 运行环境，也不猜测未经 SDK 验证的 triple。
现有模型不能表达的新机制仍可能需要最小核心契约修正，不能承诺核心永远不变。

## 验证与维护

目标解析、源码生成和实际调用分开记录。当前状态以[能力说明](capabilities.cn.md)和当前 revision 的收据为准。
Linux 作业执行本轮固定 Browser Mono Wasm/AOT aggregate 与回调回归一次；其他环境没有实际证据时保持未验证。

中立 Engine、Analysis、NativeBridge 和 CSharp 不引用具体宿主、目标或 frontend 实现。
基础实现及按需扩展归 `extensions/`；准确工程边界由[依赖矩阵](../eng/architecture/project-dependencies.json)检查。
