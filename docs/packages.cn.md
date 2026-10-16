# 包与分发

[English](packages.md) | [文档](README.cn.md)

BGCS 3.0 只有四个用户产品。工程拆分不要求用户手动拼装更多功能包。

| 用途 | 产品 | 内容 |
| --- | --- | --- |
| 嵌入式生成与可选构建集成 | `BGCS` | Host、分组配置、分析、C++ 桥接、C# backend、原生构建、独立 Clang worker、schema 和 MSBuild TaskHost |
| 中立插件与模型工具 | `BGCS.Contracts` | 不可变原生、桥接、Interop、托管模型及扩展端口；不依赖生成器、解析器或 Runtime |
| 生成绑定的消费项目 | `BGCS.Runtime` | 模块 lease、函数表、拥有型句柄、回调、缓冲区、编码与分配支持；不依赖生成器或解析器 |
| 命令行 | `BindGen-CS` | 通过 `bindgen-cs` 调用统一 Host 流水线 |

全部产品使用 .NET 10。`BGCS.CSharp`、`BGCS.Cpp2C` 等内部程序集随 SDK 交付，不要求用户额外安装独立功能产品。SDK 实体包由 `distribution/BGCS.Sdk.Package` 收集；`src/BGCS` 实现工程不单独打包。

## 安装与生成

3.0.0 实际发布之后可使用以下安装命令；此前请使用源码或下文的本地包验收方式：

```bash
dotnet tool install --global BindGen-CS --version 3.0.0
bindgen-cs init include/native.h --language c
bindgen-cs doctor bindgen.json
bindgen-cs generate bindgen.json
bindgen-cs build bindgen.json
```

C++ 使用同一个分组配置和 `generate` 命令。`bridge` 通过 Host 只请求原生桥接产物，不要求选中的所有 ABI 都已经存在托管表示。

```csharp
using BGCS;

await using BindingHost host = BindingHost.CreateDefault();
GenerationResult result = await host.GenerateAsync(
    GenerationRequest.FromFile("bindgen.json"));
if (!result.success)
    throw new InvalidOperationException(result.GetDiagnosticSummary());
```

普通插件只需要 `BGCS.Contracts`。Clang 特有的事实扩展在 worker 内执行，使用 SDK 中的 frontend 高级引用面。

## 实体包与原生资源

四个产品可以包含必要的技术分包：

- 工具入口包引用 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64` 六个实现分包。
- LLVM 20 上游解析器 runtime 包提供五个宿主闭包。
- `BGCS.ParserRuntime.osx-x64` 补充上游缺失的 Intel macOS 闭包，包括依赖动态库与许可证。

解析器资源根据运行生成器的宿主选择，与生成目标分离。Windows 上生成 Android 时，解析器仍是 Windows 版本，目标头文件与 sysroot 来自 Android SDK。Clang builtin headers 不替代目标系统 SDK 或 C++ 标准库。

SDK 包含 worker 托管文件、逐宿主 hash 清单与独立 TaskHost 依赖。构建集成收集恢复后的原生资源，按精确清单验证并部署完整 worker 目录。已完成部署的 SDK 应用运行解析器时不需要 NuGet 全局缓存。

打包器明确合并所有公开 SDK 模块的 restore 依赖图，因此删除 Host 对 frontend 的工程引用时，不会顺便丢掉 Clang 高级扩展的编译依赖。worker 与 TaskHost 的执行闭包仍然独立；许可证也按全部交付组件收集，不依赖 Host 恰好引用了什么。`BGCS`、`BGCS.Contracts`、`BGCS.Runtime` 必须各有声明的 `.snupkg`；缺失 symbol 或归档包含同名重复 entry 都会验收失败。

`distribution/manifests/native-parser-assets.lock.json` 固定五个上游宿主的包身份、实际原生文件 hash 和 CPU，同时固定 Intel 补充包的 ClangSharp 源码 commit 与 LLVM20.1.8 源码归档。Intel bootstrap 将重定位、签名后的 dylib 字节、许可证和实际 Homebrew recipe 写入 `runtime-provenance.json`，打包器验证完整文件集合与当前 BGCS revision。这份来源记录描述构建输入和字节；独立的 Intel 调用门禁还必须通过，CI 才封存并分发产物。没有真实 Intel 构建时不能发布完整产品，ARM64 动态库也不能替代。

## Runtime 包与源码

生成绑定的项目通常只引用 `BGCS.Runtime`。`runtime.mode = "source"` 输出所需源码闭包，默认使用 API 局部 namespace；单文件输出可将该闭包合入 `Bindings.cs`。两种模式通过实际原生调用消费项目验证。Runtime-only 项目的依赖图不得出现 Clang 或生成 SDK。

## 打包与验收

`eng/pack.py` 根据真实 `.deps.json` 和已恢复项目依赖图收集执行闭包，生成产品集合，并验证每个实体包小于 250 MiB。完整打包必须明确提供已经验收的 Intel macOS 解析器资源：

```bash
python3 eng/pack.py --parser-osx-x64 artifacts/clang-runtime/osx-x64 --verify-repeat-content
python3 eng/verify-packages.py --version 3.0.0
```

`--host-only <rid>` 与验收器 `--sdk-only` 是本地部分检查，不代表完整产品认证。完整安装消费验收在同 revision 的 CI 矩阵通过后，在三个首发桌面宿主和独立 Visual Studio2026 runner 分别执行。

Visual Studio 门禁使用[官方 `windows-2025-vs2026` runner image](https://github.com/actions/runner-images)，发现真实 VS2026 `MSBuild.exe`，要求 major18，并通过原生构建 / 调用 / 清理 / 重建消费工程执行 .NET TaskHost。`dotnet msbuild` 的证据单独记录，不能替代这个宿主。

安装门禁检查嵌入式 C/C++ 生成、Contracts-only 插件、Runtime-only/源码/单文件的实际原生调用、NuGet 原生 RID 选择、MSBuild 启用与默认关闭、完整拥有型清理后重建、独立 TFM inner build、静态项目图和已安装工具命令。CLI 与 MSBuild 使用同一个带 hash 的源码贡献插件 manifest，通过真实调用检查其生成头文件、原生源码和托管辅助类型。完整认证还将包模式、源码模式和单文件消费工程发布为 NativeAOT 可执行文件，再以已安装工具和 Runtime 执行完整 113 项 ABI、aggregate、C++ 与生命周期矩阵。浏览器解释执行和 AOT 两种部署各自要求三种导入模式的完整 74 项调用矩阵，以及独立的 33 项非阻塞 retained/async 生命周期子报告，并调用生成的静态 adapter。安装验证器核对实际调用与生命周期子报告一致；每套矩阵记录原始收据 hash，必须使用同一套已锁定的已安装生成器、完整执行闭包与 Runtime 字节。这需要 .NET 10 `wasm-tools` 与 Chromium。CI 使用选定 SDK 自带的工作负载清单并禁止隐式更新；收据记录实际 SDK 与 workload set。

安装消费 runner 还会只引用实际交付的 `BGCS` 包编译 Clang 高级事实插件，再让 SDK、CLI、MSBuild 使用同一个带 hash 的 worker-only manifest。插件读取 live cursor、贡献中立文档，并检查 live context 在调用结束后失效；SDK 宿主不得加载插件程序集，生成结果由 Runtime-only 消费工程编译。增加门禁代码不代表它已经通过，仍须真实的新包安装执行。

本地部分检查中，`--native-aot` 启用三种 Add 基础调用发布。搭配 `--sdk-only --host-tool`，`--native-feature-matrix` 与 `--wasm` 分别显式启用完整实际调用矩阵。部分产品集合不会因为调用通过就变成完整认证。

`--verify-repeat-content` 从同一套已编译程序集重复打包，逐项对比全部归档文件名和解压后的字节，包括元数据和 symbol。ZIP 容器时间戳可能不同；收据同时记录两次归档 hash 与一致的内容摘要。临时消费工程与打包暂存目录在运行结束时清理；保留收据与日志。

打包与安装消费收据同时记录工作区源码内容摘要、Git commit 和改动状态。完整认证要求干净工作区、相同源码字节和未变化的实体包。对已经改过的文件继续编辑仍然会改变摘要，单凭 commit 不能证明本地字节。来源回归门禁使用真实的临时 Git 仓库，拒绝源码摘要不匹配、打包期间源码变化和归档字节被修改的情况。

安装消费 restore 使用临时工作目录内独立的 NuGet packages 目录与 HTTP cache。反复验证产生的唯一测试版本不会积累到用户永久的全局缓存，先前安装的 BGCS 包也不能替代本次产品 restore。可用 `BGCS_MINIMUM_FREE_BYTES` 指定更严格的正数剩余空间下限。

已安装命令失败时，验收器在清理消费者前保留有上限的文本复现输入：每文件最多 2 MiB、合计最多 8 MiB，列出被省略的文件，排除二进制和缓存目录。超时或取消会终止自己创建的进程组 / 进程树，并在清理前保留命令日志。

打包收据记录观察到的源码 revision、工作区是否有改动、构建配置、是否使用已有输出，以及真正交付的执行资源 hash。安装验收在 restore 前，根据收据验证完整实体包与 symbol 文件集合。完整认证要求当前 revision 的干净打包；本地有改动的快照保持明确的部分验收状态。这些记录识别实际交付字节，并不声称任意旧的 `--no-build` 输出已经从当前源码重新构建。

发布前必须审核 `implementationReady` 源码声明，再验证同 revision 的功能、配置收据和全部强制 CI、产品安装 job。仓库中的 `releaseReady` 保持 false；只有完整发布预检能推导可发布结论，具体见[审核与认证状态流](release-evidence-state-flow.md)。本地构建成功或部分 SDK 包成功不满足这些发布条件。
