# 产品与资源交付

| 产品 | 用途 |
| --- | --- |
| BGCS | 完整生成库与基础组合。 |
| BGCS.Core | 独立的中立模型和生成扩展接口。 |
| BGCS.Runtime | 生成绑定在应用中使用的独立运行辅助。 |
| BindGen-CS | 使用同一生成流程的 CLI。 |

Engine、Analysis、NativeBridge、CSharp、Infrastructure 等内部程序集随完整产品交付，不要求用户逐个安装。4.0 没有 BGCS.Defaults、Contracts 别名包或 BGCS MSBuild 接入。

## 按需官方实现

| 包 | 职责 |
| --- | --- |
| BGCS.Extension.BrowserWasm | Emscripten 输入、Mono Wasm profile、DllImport/LibraryImport 与回调策略；明确拒绝 FunctionTable。 |
| BGCS.Extension.NativeAot | NativeAOT profile 以及已有调用/thunk 机制。 |
| BGCS.Extension.Android | NDK 目标和头文件环境。 |
| BGCS.Extension.AppleMobile | 设备/模拟器目标与 SDK 输入。 |
| BGCS.Extension.FreeBsd | 目标与显式 sysroot 输入。 |

BGCS 不依赖这些包。安装后必须显式注册；不会自动改变全局状态。Android、Apple mobile、FreeBSD 的目标解析本身不提供 managed runtime，也不证明实际调用。需要时必须另行提供适用的 runtime/ABI 实现。能力声明不等于运行回执。

```csharp
using BGCS;
using BGCS.Extension.NativeAot;

await using BindingHost generator = BindingGenerator.CreateBuilder()
    .AddPlugin<NativeAotPlugin>()
    .Build();
```

配置再选择对应 runtime ID。自定义实现可直接写在自己的 C# 生成项目中，无需独立插件包。

## 解析资源

完整 SDK 通过普通 NuGet 内容资产交付锁定的 worker 托管闭包和资源清单；固定 parser runtime 依赖提供原生库。CLI 携带自己的宿主闭包。资源由实际生成器宿主决定，与绑定目标分离。

首次使用时校验资源，并按版本、宿主和内容身份在用户本地 BGCS 目录准备。不会联网下载、从全局 NuGet 缓存补文件、随机选系统 LLVM 或执行编译器。高级宿主仍可显式指定部署好的 worker 根。

普通应用 build/publish 输出移动到干净位置后仍须具备完整生成资源。特殊发布形式需显式安排资源部署，不能一概宣称支持。

`BGCS.ParserRuntime.osx-x64` 是让解析器在 Intel Mac 上运行的技术资源包。
当前锁定的上游依赖提供其他五种宿主资源；这个包补齐 Intel macOS 资源，
并携带实际制作来源和许可证。它不是绑定目标 SDK 或额外生成功能，
只运行生成绑定的应用不需要它。`distribution/` 定义 BGCS 自身发布包，
`eng/` 存放仅用于维护本仓库的验证和打包工具。

## Runtime

应用通常只引用 BGCS.Runtime；它不依赖 Core、Clang、Roslyn、MSBuild 或生成器。`runtime.mode = "source"` 在 API 自己的 namespace 输出所需源码闭包，也可合入单文件。两种形式遵守相同生命周期契约。

## 仓库包验收

`eng/pack.py` 制作候选包，`eng/verify-packages.py` 验证真实归档。候选版本绑定 commit 和源码摘要。隔离消费者只使用包内容，不以工作区 DLL 替代。门禁检查闭包、许可、parser 来源、扩展激活、Runtime-only 和资源重定位。

这些脚本制作 BGCS 自身产品，不是消费项目打包 API。见[验收](acceptance.cn.md)与[发布](publish.cn.md)。
