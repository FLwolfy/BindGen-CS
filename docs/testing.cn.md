# BGCS 3.0 测试流程

[English](testing.md)

测试分别证明模型与生成回归、真实原生执行及安装后的产品行为。一个宿主上的托管编译
不能认证另一种 ABI、浏览器部署或包布局。当前完成状态见
[实施状态](implementation-status.cn.md)，必需发布门禁来自
`eng/acceptance/required-release-gates.json`。

## 环境前提

- .NET 10 SDK/runtime；生成消费者使用 .NET 10 / C# 14。
- 锁定的宿主 parser 与 worker 闭包；目标头文件及原生编译器是独立前提。
- Python 3.10 或更新版本，以及 C++23 编译器和匹配的标准库。
- CMake 执行要求 CMake 3.24 或更新版本及 Ninja；CI 固定 Meson 1.8.3。
- 浏览器执行要求所选 .NET 10 SDK 的 `wasm-tools` 及 Chromium、Chrome 或 Edge。
  桌面 NativeAOT 要求宿主的原生编译与链接工具。

macOS Intel 需要 `scripts/setup-macos-x64-clang-runtime.sh` 生成的匹配资源补充。
资源 hash 参与闭包验证；全局另一版本 LLVM 不能替代锁定 parser。
原生 provider 使用与解析目标相容的编译器和 SDK。

## 命令与证据

| 范围 | 命令 | 必需证据 |
| --- | --- | --- |
| 回归套件 | `dotnet test BindGen-CS.slnx -c Release` | 相关测试全部通过，包括失败与并发情况 |
| schema 文档 | `python scripts/generate-config-docs.py --configuration Release --check` | 仓库 schema 及双语字段表与已编译 CLI 一致 |
| 浏览器解释执行 | `python scripts/test-wasm-bindings.py` | 三种导入的全部 74 项实际检查 |
| 浏览器 AOT | `python scripts/test-wasm-bindings.py --aot` | AOT 发布后相同的 74 项检查 |
| 桌面 NativeAOT | `python scripts/test-native-aot-bindings.py` | 原生可执行程序中的全部 113 项检查，包含独立 aggregate 原生对照 |
| 安装产品 | `python eng/verify-packages.py --help` | 准确产品集合、安装后的消费者及完整调用矩阵 |
| 性能 | `scripts/test-performance-budget.sh` | 受控 10,000 声明冷/热时间及内存预算 |
| 发布清单 | `eng/acceptance/required-release-gates.json` | 同一 revision 的全部必需门禁 |

fixture 脚本默认构建需要的仓库工程。共享 checkout 先完成串行的 solution/Tool/worker
构建，再使用 `--no-build --configuration Release` 部署已封闭的输出。
`--tool-path` 与 `--runtime-path` 必须一起选择准确安装产品。其他消费者仍在运行时不能
覆盖其使用的程序集。

干净产品验收从刚打包的本地 NuGet 源恢复，验证嵌入式 C/C++ 生成、Contracts-only 插件、
Runtime-only/package/source/single-file 消费者、完整 worker/TaskHost 闭包、六种工具宿主资源、
MSBuild 显式启用与默认关闭、所属输出 clean/rebuild、多 TFM 和静态项目图。
Visual Studio 2026 门禁检查真正 `msbuild.exe` major 18，不能用 SDK-hosted MSBuild 替代。
完整安装认证还执行 NativeAOT smoke、完整 113 项 NativeAOT 及两种浏览器矩阵。
显式局部验收即使全部通过，也保留 `completeProductSet=false`。

## 独立调用 fixture

`tests/fixtures/NativeApi` 提供可移植 C17；`tests/fixtures/CppFacade` 提供独立 C++23 头文件。
公开统一 CLI 产生类型化 bridge、重新解析，再生成 DllImport、LibraryImport、FunctionTable
三种实例 API。消费者编译准确托管产物，harness 不手工修补生成源码。

| 每种导入重复的检查 | 必需结果 |
| --- | --- |
| 标量与 64 位整数 | 符号算术及高位保留 |
| 宽 enum 表现 | 固定宽度及独立调用的原生载体 |
| 原生 `long`/bool | 目标宽度、符号算术及 boolean 字节 |
| record 布局 | 原生 size/offset/指针宽度及 context 指针保留 |
| 混合位字段 | signed、unsigned、enum 经原生修改后正确 |
| buffer 与容量不足 | 准确内容/count；失败时无写入 |
| 原地址别名 | 原生指针相等，ref/ref、in/out、记录字段和 Span/ref 的有序修改 |
| opaque handle | 类型化创建/调用/析构及原生分配计数 |
| native/null callback | 准确指针/state/result/count 及明确 null 拒绝 |
| 生成托管 callback | 静态 userdata thunk 保留捕获状态，原生对象指针明确保持 unsafe，原始托管异常经边界返回 |
| release/null handle | 无存活分配，null 析构安全 |
| C++ 构造/方法 | status/error 边界及对象状态 |
| 多继承 | 调整基类地址、还原 owner、拒绝无关下转型 |
| C++ 析构 | owner 释放及 null 清理安全 |

每种导入贡献 23 个真实检查标签。另外五项检查缺失符号、拥有型 provider 释放、失败的表安装、
借用 provider 与外部指针存储，共 **74 项必需检查**。收据必须包含实际调用和断言之后的准确全集。NativeAOT 额外执行三种导入共 39 项 aggregate 检查，合计 113 项；每种导入的 13 个结果形状都须与独立原生 C probe 一致。

原始 C++ 头文件的隐式默认构造保持参与测试，不能插入显式构造逃避未支持的生成。
C 源码保持 C17；未实现的托管表现单独登记，不能换成编译器特有 enum 扩展。
当前 fixture 通过 `api.enums` 和 `api.enumMappings` 生成 `WideFlags`，实际检查 friendly enum 与未改变的 raw `ulong` ABI。59/60/63 位和完整 UInt64 范围经独立 C17 实现往返，不使用手写 enum 代替生成。
FunctionTable 使用 `INativeModuleProvider`、`NativeModuleSession`、`ExternalFunctionTableProvider`。
浏览器还调用生成的 `FromStatic()`。lease 保留原生清理能力，并拒绝 context 关闭后的调用。

## 浏览器链接与执行

harness 查询所选 workload 的实际 compiler/sysroot，通过 `NativeFileReference` 链接 C 与生成的
C++，发布应用并在所属 headless profile 中执行。原生模块身份来自声明的文件名：
`api.c` 注册 `api`，`libapi.a` 注册 `libapi`；静态链接不能默认所有 runtime 都使用 `__Internal`。

```bash
python scripts/test-wasm-bindings.py --dotnet /path/to/dotnet --browser /path/to/chromium
python scripts/test-wasm-bindings.py --aot --dotnet /path/to/dotnet
```

收据记录真实浏览器、SDK、workload 版本。Windows x64、Linux x64、Intel macOS 分别执行解释与 AOT。
中立原生 provider 门禁独立验证混合 C17/C++23 archive 与可重定位 object，设置准确 workload 路径和
`BGCS_REQUIRE_WASM_NATIVE=1`，缺失前提会失败。

## 桌面 NativeAOT 与故障注入

```bash
python scripts/test-native-aot-bindings.py --dotnet /path/to/dotnet --compiler /path/to/clang
python scripts/test-native-aot-bindings.py --inject-native-error
python scripts/test-wasm-bindings.py --inject-native-error
```

NativeAOT 构建 fixture library，发布独立原生程序并执行。两种 runner 使用
`DisableRuntimeMarshalling` 及相同 unsafe ABI 断言。注入修改复制的标量实现，真实调用必须断言失败，
门禁也必须失败；编译失败不能替代这项真实调用失败证明。

缺失前提、解析/链接失败、AOT compiler crash、超时、错误指针宽度及不完整检查均失败。
不能因依赖问题回退成成功。编译器 workaround 保留浏览器全部 74 项或 NativeAOT 全部 113 项断言和准确工具链身份；
诊断探针成功不能认证完整 fixture。

## 收据、清理与发布事实

每次所属运行保留分组配置、native 输入、生成源码、日志及 `report.json`。
收据包含 revision、dirty 状态及实际 tool/worker/runtime hash。成功或失败都清理所属的大型
`bin`、`obj`、发布输出、generator 部署与浏览器 profile；`--keep-build-output` 显式保留诊断现场。
清理不能触及无关用户输入。

语义/ABI/cache、worker crash、发布恢复、路径与 Runtime 生命周期持续作为回归门禁。
真实库输入采用锁定 revision 与独立源码/API 快照，候选必须审查，不能自动提升为基线。
历史 2.x 收据、旧测试名称、配置的 CI job、dirty-tree 局部运行、managed-only 通过均不能替代
完整同 revision 发布清单。
