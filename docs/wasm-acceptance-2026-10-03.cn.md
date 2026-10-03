# BGCS 独立 Wasm 验收报告 — 2026-10-03

[English](wasm-acceptance-2026-10-03.md) | [Wiki](README.cn.md) | [独立测试流程](testing.md#independent-webassembly-invocation)

## 验收结论

BGCS 的通用 target → parser → analysis → IR → emitter 分层保持健康，
Wasm 原生调用已经有属于 BGCS 自身的独立证据。本次发现并修复了两个通用缺陷，
所有本次检查通过。跨平台目标与可替换的 `INativeContext` 路径保持独立，未新增游戏引擎依赖。

| 项目 | 结果 |
| --- | --- |
| 独立浏览器原生调用 | 三种 import mode，共 **26 项通过** |
| 全部本地测试项目 | **11 个项目、716 项通过、0 失败、0 跳过** |
| Release 解决方案构建 | **0 警告、0 错误** |
| 原生错误结果故障注入 | 正确失败，没有假通过 |
| README 的源码 CLI 流程 | SDK 10 下 `init → generate → build` 全部通过 |
| 默认 / 自定义 Runtime namespace | 默认 / 自定义 context 的四组编译检查通过 |

测试 C API、浏览器消费项目、符号解析器和断言均属于 BGCS 仓库。
验收不借用任何消费应用的集成结果，也不需要游戏引擎源码或 runtime。

## 实际运行环境

| 部分 | 本次实测 |
| --- | --- |
| 作者主机 | Windows 11 x64，build 26200 |
| 浏览器 | Edge 154.0.4258.53，headless，独立 profile |
| Wasm SDK / runtime | .NET SDK 9.0.318 / runtime 9.0.20 |
| 原生工具链 | .NET `wasm-tools`，Emscripten 3.1.56 |
| 输出目标 | `emscripten-wasm32-emscripten`，指针宽度 32 位 |
| C# 执行 | Mono WebAssembly runtime；未启用 managed AOT |
| BGCS parser | ClangSharp 20.1.2.4 / libclang 20.1.2，builtin headers 20.1.8 |
| README 源码命令验证 | .NET SDK 10.0.401 |

builtin headers 与目标 SDK 的职责不同：前者配套解析器，后者提供目标的系统头文件、
标准库及实际编译 / 链接工具。这里明确记录 parser 与 builtin 的 patch revision；
当前 fixture 通过不代表任意 SDK / header 组合均已认证。

## Wasm 实际验证内容

测试先构建 BGCS，再自动生成三套 bindings，编译 C# 消费项目和自有 C 实现，
将原生代码链接进 Wasm runtime，通过本地 HTTP 在真实浏览器中运行断言。
生成代码直接参与编译，没有为验收手改生成物。

`DllImport`、`LibraryImport`、`FunctionTable` 每种分别检查：

1. 带符号参数和返回值的准确性。
2. record size、context offset、指针与 `size_t` 宽度，以及字段往返值。
3. 输入 / 输出 buffer 的内容和数量。
4. 容量不足时失败、输出数量为零、整个 buffer 不被修改。
5. opaque handle 的强类型创建与调用。
6. C → C# 回调的 handle、参数、state、返回值及调用次数。
7. null callback 不被调用并明确失败。
8. 释放后原生活跃对象为零，null handle 操作安全。

三种模式合计 24 项，再加上 missing symbol 和 function-table context 仅释放一次，
共 **26 项**。符号目录属于 fixture，通过现有公开 `INativeContext` 接入；
BGCS Runtime 没有新增浏览器专用 registry。

## 修复内容与通用性

| 问题 | 修复 | 验证 |
| --- | --- | --- |
| 借用的函数指针数组被当成自有内存释放；重复 cleanup 的所有权不清楚 | 明确借用 / 自有存储，禁止调整借用数组，释放幂等，检查边界与释放后访问，新增槽清零 | 调用方内存仍可读写，context 只释放一次，越界与释放后访问被拒绝 |
| 用户 namespace 包含 `FunctionTable` 时遮蔽 Runtime 类型 | 所有输出布局按配置的 Runtime namespace 完整限定类型名 | 默认 / 自定义 Runtime、默认 / 自定义 context 的四种组合编译通过，Wasm 调用通过 |

两项都是通用 Runtime / emitter 修正，没有 Wasm 专用修复分支，也未增加公开 API。
所有权行为已同步到 [API 文档](api.md)和[包说明](packages.cn.md)。

测试应用另外修正了与原生输入对应的 module name（`api`），以及 Edge launcher 的进程管理。
这些配置属于测试宿主，不改变 BGCS 核心 ABI 和加载策略。

## 失败路径与 README 验证

在独立临时副本中故意让 C 的加法返回错误结果，浏览器准确报出
`DllImport: scalar arguments/result.`，结果为失败、零通过项。
正向 fixture 和生产源码均保留，证明验收确实验证了原生运行结果。

中英文 README 已改为先解释“头文件 / 原生库 / C# bindings”，再给出首次接入流程，
明确 `build` 只编译检查 C#，原生库仍需编译和部署。补齐 C++、导入模式、安全策略、
扩展、跨平台和 WebAssembly 的职责说明，移除了首页的状态 emoji 表。

源码 CLI 的 `init → generate → build` 使用复制的 QuickStart header，在 SDK 10.0.401
实际完成，消费项目为零警告 / 零错误。另有 SDK 9 下的已编译 CLI 同流程验证。
没有覆盖本次任务范围外的发布包或 SDK 10 打包 gate。

## 可复查证据

以下路径均相对 BGCS 仓库根目录，产物保留在被 Git 忽略的 `artifacts/` 中：

| 证据 | 路径 |
| --- | --- |
| 完整 Wasm 正向报告 | `artifacts/wasm-acceptance/47946b86250f47139760c77a6a004f9a/report.json` |
| 原生故障注入报告 | `artifacts/wasm-negative/6c15c70adbed4843bf185419ac2350a2/negative-report.json` |
| 11 项目的 TRX 与日志 | `artifacts/wasm-managed-regression/` |
| README 源码命令验证 | `artifacts/readme-source-2b115e104f4a455a915ae1af39146ae5/` |
| README 已编译 CLI 验证 | `artifacts/readme-quickstart/53f1fae2bede4d069feb59449e2d2842/` |

Wasm run 目录另含生成配置、bindings、原生源码、consumer、SDK 查询和 generation / publish 日志。
复现命令、依赖和检查表见[独立测试流程](testing.md#independent-webassembly-invocation)。
本次未提交或推送代码。

## 清晰的验收边界

Windows、Linux、Intel macOS 的独立 CI job 已配置并上传各自主机证据。
本次实际执行的是 Windows / Edge；其他主机的 job 需运行成功后才能标记通过。
Firefox / Safari、managed AOT、C++ / STL Wasm 语义、移动设备和发布打包仍需要各自验收。
桌面目标的完整发布矩阵及原生快照也继续保留独立 gate。

当前检查支持“这次修改的架构方向健康、独立 C ABI Wasm 调用通过”的结论。
无法用这份报告保证所有平台、库与组合都不存在问题，发布成熟度仍依据[验收规范](acceptance.cn.md)。

Windows 主机没有指定的 `/System/Library/Sounds/Glass.aiff`，本次未播放提示音。
