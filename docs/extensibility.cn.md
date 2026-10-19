# BGCS 3.0 中立扩展

本说明对应实施中的新 Host API。正式发布状态见
[实施状态](implementation-status.cn.md)；尚未完成的端口不能被视为已认证能力。

普通插件只引用 `BGCS.Contracts`，实现
`BGCS.Contracts.Plugins.IBindingPlugin`。源语言事实、桥接计划、互操作语义和
候选产物读取都通过中立契约传递，不要求引用 Clang frontend。

插件描述必须声明 ID、semantic version、Contracts major/minor 和完整服务清单。
服务 ID 在 Host 内全局唯一。变换、lowering 和后处理端口按 priority 降序执行，同优先级按 ordinal ID 排序。
目标 provider 按 ordinal ID 查询；多个 provider 声明同一个目标时拒绝，不按 priority 选择。
frontend、projection 和 backend 分别只能注册一个主实现。

Host 先创建发现实例并读取描述，随后释放该实例。每个 generation session
创建独立插件，`Configure` 注册服务工厂，然后冻结注册表。每次请求服务的工厂
必须返回独立、可释放的实例。服务结束后释放，插件在所有服务之后释放。
保存 `IExtensionRegistryBuilder` 并在 Configure 返回后继续注册会明确失败。

```csharp
public sealed class DomainPlugin : IBindingPlugin
{
    public PluginDescriptor descriptor => new(
        "example.domain", "1.0.0",
        [new("example.domain-type", ExtensionPort.CppTypeLowering, 20)]);

    public void Configure(
        IExtensionRegistryBuilder registry,
        PluginConfiguration configuration)
    {
        registry.RegisterFactory<ICppTypeLowering>(
            "example.domain-type", () => new DomainLowering());
    }
}
```

可运行的 Contracts-only lowering 和最终验证器示例位于
`tests/fixtures/plugins/NeutralLoweringPlugin`，实际 C++ 构建、桥接复核和调用由
`NeutralCppBridgeTests` 执行。

SDK 可以调用 `AddPlugin<TPlugin>()`，或者提供 `AddPluginFactory`。
需要部署到独立加载上下文的插件使用 `AddPluginManifest`。
配置中的 `extensions.plugins` 接受同一种 manifest，不要求消费者手工安装内部模块。

只想调整内置托管投影时，可以注册 `IManagedApiTransform`，端口为
`ExtensionPort.ManagedApiTransform`；SDK 直接组合使用 `AddManagedTransform`。
它在内置或自定义主 projection 之后、C# 规划之前接收不可变 `ManagedApiModel`。
例如通过 `ManagedType.memberNames` 和稳定成员 ID 改枚举项名称，无需重新实现
所有原生类型、字符串、所有权或回调投影，也不需要引用 C# backend。
返回的新模型必须保留同一个 `InteropModule` 实例。修改 ABI 应在有对应验证的
interop 阶段处理；托管名称扩展不能悄悄替换 ABI。

此端口沿用相同的全局 ID、priority、会话生命周期、反向释放和完整指纹规则。
`tests/fixtures/plugins/NeutralLoweringPlugin/ManagedNamesPlugin.cs` 展示一个仅引用
Contracts 的实现；`ManagedTransformTests` 验证缓存状态变化、释放及失败时保留产物。
这些是仓库实现检查，产品包安装认证仍按发布门禁单独执行。

`ISourceContributor` 在原始 C/C++ 解析之前贡献生成输入。它与产物 processor 的
区别在于：贡献的公开头文件能引入声明，再经过普通的声明选择、分析、lowering 和
C# 生成。SDK 与 CLI 接受同一份 Contracts-only 插件 manifest。
当前实现由仓库测试覆盖；安装认证与正式发布状态仍以实施状态文档为准。

```csharp
public PluginDescriptor descriptor => new(
    "example.generated", "1.0.0",
    [new("example.generated-inputs", ExtensionPort.SourceContributor)]);

public void Configure(IExtensionRegistryBuilder registry, PluginConfiguration settings)
    => registry.RegisterFactory<ISourceContributor>(
        "example.generated-inputs", () => new GeneratedInputs());
```

服务接收冻结的 `SourceContributionRequest`，包含具体目标、有效原始输入、库身份和
托管/Runtime namespace 和所选 `apiStyle`。请求不暴露私有候选目录与正式输出目录。返回值
`SourceContribution` 提供完整不可变字节；Host 在暂存前再次防御性复制。
贡献必须带有显式 `Declared` 或 `UserAsserted` 来源。生成文本不会自动获得
“原生行为已验证”的证据等级。

托管贡献扩展生成 API 的 partial 类型时，应读取传入的 API 风格：实例 API 和静态
API 需要不同的声明。独立托管辅助类型可以使用自己的类型名。服务 ID 允许便携的
斜杠分层，例如 `example.generated/helpers`；完整 ID 参与每个逻辑输入身份和输出路径。

| 角色 | 要求的语言 | Host 管理的输出 |
|---|---|---|
| `PublicHeader` | `C` 或 `Cpp` | `native/include/contributions/<service>/<path>` |
| `NativeSource` | `C` 或 `Cpp` | `native/src/contributions/<service>/<path>` |
| `ManagedSource` | `None` | `managed/Extensions/<service>/<path>` |
| `Resource` | `None` | `native/resources/contributions/<service>/<path>` |

公开头文件设置 `exposeToBindings` 后，其中的声明作为普通绑定入口参与选择。
未暴露的头文件可以作为已暴露头文件的依赖。原生源码后缀和 media type 必须与
显式语言一致；C 解析不能暴露 C++ 头文件。资源按字节复制，不会被隐式编译。
原生源码通过生成 include 根目录，用
`#include "contributions/<service>/<path>"` 引用贡献的头文件。

原生逻辑来源固定为 `contributions/<service>/<path>`，不依赖物理暂存目录。
文件、完整目录成员和 hash 进入生成 artifact manifest。外部构建消费发布的原生源码与头文件。托管辅助源码参与最终语法与导入契约复核；单文件模式把它合入 `Bindings.cs`。无效贡献不会替换上一套完整输出；消费者编译与原生构建由外部构建系统验证。

每个原始目标解析在查询 parser cache 之前调用贡献服务一次；bridge reparse 不会
再次调用。生成字节参与缓存阶段身份，即使服务申报的私有指纹没有改变，也不会复用
与当前字节不符的结果。实现 `ICacheFingerprintProvider` 并声明外部输入后，符合条件的
阶段才能使用缓存；缺少指纹的贡献服务会禁用受影响的 emission 复用。
服务接收当前会话取消 token，字节捕获完成后、解析和发布前释放。
Host 校验可移植路径、大小写冲突、角色与 media type 一致性和严格 UTF-8。

manifest 显式指定 entry type 和 assembly。`assets` 必须列出完整 managed/native
执行闭包及各文件的 SHA-256；存在的 `.deps.json` 也必须列入。相对路径不能穿越
manifest 目录，符号链接不能逃逸。修改已经锁定的输入，需要创建新请求或新 Host。

确实需要读取 Clang 专属信息时，实现
`BGCS.Frontend.Clang.Advanced.IClangFactContributor`。这个高级接口需要完整 SDK 中
与解析器匹配的 frontend 引用；普通 lowering、目标、托管和产物扩展仍然只需 Contracts。
frontend 仅导出 `ClangFrontend` 与三个有作用域的 Advanced 入口类型。可变解析模型和
AST 构建辅助类均为内部实现。`eng/architecture/test-friends.json` 只登记唯一的
`BGCS.Frontend.Clang.Tests` 测试友元，用于实现回归；架构门禁拒绝生产友元、未登记
的声明及伪装测试程序集。
插件入口仍实现 `IBindingPlugin`，但只声明 `ExtensionPort.FrontendFactContributor`
服务，并通过确切的高级接口注册：

```csharp
registry.RegisterFactory<IClangFactContributor>(
    "example.clang-facts", () => new DomainClangFacts());
```

manifest 必须设置 `workerOnly: true`，`workerParserIdentity` 必须等于
`bindgen-cs version --json` 输出的 `parser.expectedIdentity`。当前值为
`ClangSharp/20.1.2.4;LLVM/20`；升级解析器后需要重新构建并验证高级插件。
Host 只读取已锁定的描述、schema 和设置，不在宿主中加载它的实现。worker 在私有
collectible 加载上下文中激活插件，只共享 Contracts 和 worker 自己持有的确切
frontend/ClangSharp 程序集身份；其余依赖仍必须列入带 hash 的私有资产闭包。

`Contribute(ClangFactContributionContext, NativeModule, CancellationToken)` 在
translation unit 存活期间同步执行。context 能把原始声明的稳定 ID 映射到借用的
`CXCursor`，提供原始来源位置映射，并暴露借用的 translation unit。返回值必须是
同一目标、语言、指针宽度及字节序的完整中立 `NativeModule`。方法返回后，包括
抛出异常的情况，通过 context 继续访问 native 信息会失败。复制 cursor 或
translation-unit 句柄不会延长其生命周期；不能保存这些句柄或异步使用它们。
下游普通扩展只接收已脱离 native 生命周期的中立模型。

每次解析都会调用贡献服务。通过 `context.request.purpose` 区分中立的
`SourceAnalysis` 与 `BridgeVerification`，不要根据
临时目录或文件名猜测阶段。只处理原始源码的服务应在验证请求中返回原模型。
跳过贡献仍然执行插件激活、输入捕获和指纹核对；生成桥接的 ABI 也保留独立验证。

查询受影响的缓存前，独立 worker 先发现插件和服务的指纹、外部输入，不解析 AST。
真正解析的 worker 重新捕获这些状态，与预检结果比较，并在贡献执行结束后再次核对。
发布前还会复核发现的输入。插件及服务都提供完整指纹时，符合条件的阶段才可复用缓存；
缺少指纹仍允许执行，但禁用受影响的缓存。通过 `IExternalInputProvider` 或指纹贡献
声明文件，并用冻结配置的路径解析接口定位。异常、输入变化、指纹漂移、worker 崩溃、
取消和超时都会使请求失败，不发布部分结果。

可运行示例为 `tests/fixtures/plugins/ClangFactPlugin`。`WorkerFactPluginTests`
实际验证 live cursor、context 失效、无 AST 预检、指纹及文件变化和进程故障隔离。
这些本地测试不等于最终包或其他平台认证；正式支持状态仍看发布验收收据。

加载器只共享宿主 Contracts 和框架程序集身份。其他依赖必须来自声明的私有闭包，
不会使用宿主偶然加载的同名第三方程序集。释放后请求卸载 collectible context；
用户保留插件对象或 delegate 时，实际回收仍受这些引用影响。进程隔离和加载上下文
隔离不构成安全沙箱，插件是用户明确选择执行的代码。

插件自己的设置位于 `extensions.options[pluginId].values`，由描述中的
`configurationSchema` 验证。默认 schema 只接受空对象。自定义 schema 使用
Draft 2020-12，依赖通过 `$defs` 和本地 anchors/ref 打包；不会隐式下载外部 schema。
配置 wrapper 的 `pathBase` 捕获声明文件目录，SDK 也可以显式设置它。
合并多个文件时，各设置值保留自己的 JSON pointer 路径来源。插件使用
`configuration.ResolveInputPath("/rules/file")` 或 `GetPathBase("/rules/file")`
解析特定输入；来自不同目录的设置不能通过单个 `pathBase` 静默重定位。
访问包含混合来源的共同 `pathBase` 会明确失败，验证收据保存逐值来源。

`IGenerationValidator` 在本次请求所需的 managed/native 构建检查之后运行。
`ValidationRequest` 包含具体目标、最终 ABI、确切候选清单和 `IArtifactReader`。
读取接口复核 hash、路径与 UTF-8，只允许访问声明的候选。读取上限默认 256 MiB。
错误诊断、异常、注册身份变化和服务释放失败都会阻止整个候选集合发布。

在完整输入可建立时，Host 可以复用内置 frontend 的原始解析结果和内置 C# emission。
解析阶段的资格检查包含已验证 worker 闭包、固定的 .NET host/runtime、解析器资源、
有效参数、源码内容、include 搜索顺序与目录成员关系。无法覆盖的环境或编译器输入会
禁用复用。自定义 frontend/backend 和缺少完整指纹的扩展不会静默共用内置条目。
报告逐阶段记录实际 hit、miss 或 not-used。
变换、处理器及本次请求所需的全部验证仍会执行。详见
[缓存配置](configuration-guide.cn.md#插件-bootstrap-与缓存策略)。

`IArtifactProcessor` 当前可以替换、删除已声明的托管文本候选，并在当前目标的
托管目录中新增辅助文本。editor 在结束后关闭，
更新后的 hash 随候选传递；内置 C# backend 在处理后重新读取 imports 和 storage。
自定义 backend 配合 processor 时需要自己的最终验证器。单文件模式会将新增
C# 辅助文件与原有源码重新合并，再检查导入契约。原生产物不属于此托管 editor 的支持范围，应使用明确的源码贡献或 native shim。

消费者编译、原生构建和打包属于外部工具，不是执行产品构建的插件端口。高级 worker
插件仍需验证所声明的准确 parser 兼容性与安装执行闭包。显式线程安全的 Host 服务复用仍按实施状态记录，
不能把接口存在当作验收完成。
