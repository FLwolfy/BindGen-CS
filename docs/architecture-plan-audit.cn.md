# 架构 Plan 逐项核对

[文档索引](README.cn.md) · [完整计划](platform-architecture-refactor-plan.cn.md) · [当前架构](architecture.cn.md) · [独立验收](architecture-refactor-acceptance.cn.md)

## 当前实现与证据

| 要求 | 实际边界 | 验证 |
| --- | --- | --- |
| 八个生产项目和唯一 CLI | Core、CppAst、Intermediate、BGCS、Cpp2C、Language、Runtime、Tool | 架构验证允许依赖图、唯一执行项目、禁止 linked production source |
| 通用代码规范和最小公开入口 | AGENTS、独立通用规范、`.editorconfig`、style/documentation validator | 零排版违规；3226 个公开声明的 XML 检查通过 |
| Core 中立 | target、插件、IO、缓存、集合、写出和进程契约 | 没有 CppAst/ClangSharp 依赖；公开边界测试 |
| 目标与宿主分开 | `INativeTargetProvider`、target/toolchain descriptor、Clang resolver | Windows/Unix/Apple/Emscripten provider 与 ABI 测试；Wasm 实际 4 字节指针 |
| builtin headers 与系统 SDK 分开 | 与包内 libclang 匹配的 resource bundle；显式 SDK/sysroot | 独立 parser 和工具链 discovery 回归、真实库解析 |
| 真正冻结的 IR | Intermediate 实际持有 Binding/Bridge 模型；只读集合，复制输入 | IR 测试、无源码链接、无 AST/运行服务对象，emitter 只消费冻结结果 |
| 唯一 C 和 C++ 流程 | BindingGenerationPipeline、CppBridgeGenerationPipeline | 真实 C/C++ 生成、严格编译、实际调用；没有 EmitAst 旁路 |
| 原生构建 provider 清晰归属 | Build 放计划/执行协议；Build/Providers 放五个具体实现 | 真实 clang-cl/MSBuild 导出及调用；CLI 和消费者同步当前 namespace |
| Language 和 Runtime 独立 | 具体语言前端；互操作 owner、callback 和 borrowed context | Language/Runtime 回归；三种 import mode 实际运行 |
| 输出、缓存、并发与失败保全 | Core.IO 共同 directory transaction、精确缓存校验、有界 Windows rename | 目录占用、回滚、并发、篡改及完整托管矩阵 |
| CLI、API snapshot 和 dependency audit 收口 | Tool 的 Commands、Validation 与 Output | CLI/package 干净消费者、公开 API snapshot、许可证和漏洞审计 |
| 示例和独立平台验收 | QuickStart、NativeShim、LoweringPlugin；共享 NativeApi/CppFacade fixture | NativeShim 输出 42；NativeAOT/Wasm/Wasm AOT 各 56 项真实调用；错误注入失败 |
| 性能、打包和 CI | 冷/缓存预算、两次 pack 比较、独立宿主部署矩阵 | 本机全矩阵；远程 CI 和其他宿主实机状态另报 |

## 原计划名称与共同边界

| 原计划节点 | 当前实现及原因 |
| --- | --- |
| `GenCacheFile` | 删除原空 DTO；实际缓存身份、完整 entry 和恢复由 `IncrementalGenerationCache` 管理。 |
| `Diagnostics/BindingDiagnostic.cs` | 中立诊断与结果定义位于 `BindingGenerationResult.cs`；catalog/codes/descriptor 在同一 Intermediate 项目。它们是紧密关联的结果协议，不依赖 Parser。 |
| `IPatch` | Pre/Post patch 的输入和阶段不同，分别使用 `IPrePatch` 与 `IPostPatch`；没有无行为的共同 marker。 |
| `GeneratedOutputTransaction` | 所有输出共用 Core.IO 的 `OutputDirectoryTransaction`；没有生成器专属转发事务。 |
| `CppLoweringRecipes` | 类型与 callable recipe 分开为 `CppTypeLoweringRecipe`、`CppCallableLoweringRecipe`，其关联 plan/context 按单类型文件组织。 |
| 通用 `Preprocessing/Preprocessor` | 实际 C# 条件编译由 `CSharp/Preprocessing/CSharpPreprocessor` 处理；C/C++ 预处理属于 Clang。 |
| `tests/wasm/Native/api.h / api.c` | 桌面、NativeAOT 和 Wasm 使用同一 `tests/fixtures/NativeApi`，避免多份 ABI 向量漂移；CppFacade fixture 同样共用。 |

原生构建 provider 最终已迁入计划中的 `Build/Providers`，namespace 为
`BGCS.Cpp2C.Build.Providers`。旧 namespace 没有 compatibility alias；当前 CLI、测试和包文档同步。
此归属调整不改变生成算法、原生 ABI 或互操作 Runtime，终版完整矩阵重新验证。

本机报告只证明声明的 Windows 宿主结果。其他宿主、字节序、SDK 和 runtime 必须由自身独立向量验证；
不能把 CI 配置、静态解析成功或外部消费者成功写成已经完成的库实机验收。
